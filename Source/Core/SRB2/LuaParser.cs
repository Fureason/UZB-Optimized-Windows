
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Types;

#endregion

namespace CodeImp.DoomBuilder.ZDoom
{
	public sealed class LuaParser : ZDTextParser
	{
		#region ================== Delegates

		public delegate void IncludeDelegate(LuaParser parser, string includefile);
		
		public IncludeDelegate OnInclude;

		#endregion
		
		#region ================== Constants
		
		#endregion
		
		#region ================== Variables

		//mxd. Script type
		internal override ScriptType ScriptType { get { return ScriptType.LUA; } }

		// SRB2 mobjs
		private Dictionary<int, ActorStructure> mobjs;
		private Dictionary<string, float> statepreviewscales;

		//mxd. Includes tracking
		private HashSet<string> parsedlumps;

		//
		public bool NoWarnings = false;

		#endregion

		#region ================== Properties

		/// <summary>
		/// All mobjs that are supported by the current game.
		/// </summary>
		public ICollection<ActorStructure> Mobjs { get { return mobjs.Values; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public LuaParser()
		{
			// Syntax
			whitespace = "\n \t\r\u00A0"; //mxd. non-breaking space is also space :)
			specialtokens = "=\n";
			skipregions = false; //mxd

            ClearActors();
		}
		
		// Disposer
		public void Dispose()
		{
			mobjs = null;
			statepreviewscales = null;
		}

		#endregion

		#region ================== Parsing

		protected internal override void LogWarning(string message, int linenumber)
		{
			if (NoWarnings)
				return;
			base.LogWarning(message, linenumber);
		}

		// This parses the given Lua stream
		// Returns false on errors
		public override bool Parse(TextResourceData data, bool clearerrors)
		{
			//mxd. Already parsed?
			if(!base.AddTextResource(data))
			{
				if(clearerrors) ClearError();
				return true;
			}

			// Cannot process?
			if(!base.Parse(data, clearerrors)) return false;

			// Continue until at the end of the stream
			while (SkipWhitespace(true))
			{
				// Read a token
				string token = ReadToken();

				if (!string.IsNullOrEmpty(token))
				{
					string objname = null;
					int editnum = 0;

					if (token.Contains("$SRB2_Skip"))
					{
						return (ErrorDescription == null);
					}
					else if (token.Contains("$EditInfo"))
					{
						SkipWhitespace(true);
						token = ReadToken();

						bool valid = int.TryParse(token, out editnum);
						if (!valid || editnum <= 0)
						{
							continue;
						}
					}
					else if(token.StartsWith("states[", StringComparison.OrdinalIgnoreCase) && token.EndsWith("]"))
					{
						string statename = token.Substring(7).TrimEnd(']');
						SkipWhitespace(true);
						if(ReadToken() != "=") continue;
						SkipWhitespace(true);
						string table = ReadLuaTable();
						float previewscale;
						if(TryReadStatePreviewScale(table, out previewscale))
							statepreviewscales[statename] = previewscale;
						else
							statepreviewscales.Remove(statename);
						continue;
					}
					else
					{
						if (!token.StartsWith("mobjinfo[") || !token.EndsWith("]")) continue;
						objname = token.Substring(9).TrimEnd(new char[] { ']' });

						SkipWhitespace(true);
						token = ReadToken();

						if (token != "=")
						{
							continue;
						}
					}

					SkipWhitespace(true);
					token = ReadToken();

					if (token != "{")
					{
						ReportError("Invalid object definition, missing {");
						return false;
					}

					// Read actor structure
					ActorStructure mobj = new LuaMobjStructure(this, objname, editnum);
					if (this.HasError) return false;

					mobjs[mobj.DoomEdNum] = mobj;
				}
			}

			ApplyStatePreviewScales();

			// Return true when no errors occurred
			return (ErrorDescription == null);
		}
		
		#endregion
		
		#region ================== Methods

		public ActorStructure GetMobjByDoomEdNum(int doomednum)
		{
			return mobjs.ContainsKey(doomednum) ? mobjs[doomednum] : null;
		}

		internal void ClearActors()
		{
			// Initialize
			mobjs = new Dictionary<int, ActorStructure>();
			statepreviewscales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			parsedlumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase); //mxd
		}

		private string ReadLuaTable()
		{
			StringBuilder result = new StringBuilder();
			while(true)
			{
				string line = ReadLine();
				if(line == null) break;

				int comment = line.IndexOf("--", StringComparison.Ordinal);
				if(comment >= 0) line = line.Substring(0, comment);
				result.Append(' ').Append(line.Trim());
				if(line.IndexOf('}') >= 0) break;
			}

			return result.ToString().Trim();
		}

		private bool TryReadStatePreviewScale(string table, out float scale)
		{
			scale = 1.0f;
			string action = string.Empty;
			string var1 = string.Empty;
			string var2 = "0";
			if(string.IsNullOrWhiteSpace(table)) return false;

			int open = table.IndexOf('{');
			int close = table.IndexOf('}', open + 1);
			if(open < 0 || close < 0) return false;

			string[] entries = table.Substring(open + 1, close - open - 1).Split(',');
			List<string> positional = new List<string>();
			foreach(string entry in entries)
			{
				string value = entry.Trim();
				if(value.Length == 0) continue;

				int equals = value.IndexOf('=');
				if(equals < 0)
				{
					positional.Add(value);
					continue;
				}

				string name = value.Substring(0, equals).Trim();
				value = value.Substring(equals + 1).Trim();
				switch(name.ToLowerInvariant())
				{
					case "action": action = value; break;
					case "var1": var1 = value; break;
					case "var2": var2 = value; break;
				}
			}

			// Standard SRB2 Lua state layout:
			// sprite, frame, tics, action, var1, var2, nextstate.
			if(action.Length == 0 && positional.Count >= 6)
			{
				action = positional[3];
				var1 = positional[4];
				var2 = positional[5];
			}

			int options;
			if(!action.Equals("A_SetScale", StringComparison.OrdinalIgnoreCase)
				|| !SOCParser.TryEvaluateInteger(var2, out options)
				|| (options >> 16) != 0)
				return false;

			return SOCParser.TryEvaluateFixedScale(var1, out scale) && scale > 0.0f;
		}

		private void ApplyStatePreviewScales()
		{
			foreach(ActorStructure actor in mobjs.Values)
			{
				actor.Properties.Remove("$actionscale");
				string spawnstate = actor.GetPropertyValueString("spawnstate", 0).Trim();
				float scale;
				if(string.IsNullOrEmpty(spawnstate) || !SOCParser.RunsSpawnAction(actor)
					|| !statepreviewscales.TryGetValue(spawnstate, out scale))
					continue;

				actor.Properties["$actionscale"] = new List<string>
				{
					scale.ToString(CultureInfo.InvariantCulture)
				};
			}
		}

		#endregion
	}
}
