
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
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.ZDoom
{
	public sealed class SOCParser : ZDTextParser
	{
		#region ================== Delegates

		public delegate void IncludeDelegate(LuaParser parser, string includefile);
		
		public IncludeDelegate OnInclude;

		#endregion
		
		#region ================== Constants
		
		#endregion
		
		#region ================== Variables

		//mxd. Script type
		internal override ScriptType ScriptType { get { return ScriptType.SOC; } }

		// SRB2 mobjs
		private Dictionary<int, ActorStructure> mobjs;
		private Dictionary<string, MapInfo> mapheaders;
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
		public Dictionary<string, MapInfo> MapHeaders { get { return mapheaders; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public SOCParser()
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
			mapheaders = null;
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

		// This parses the given SOC stream
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
			while (SkipWhitespace(true, true))
			{
				// Read a token
				string token = ReadToken(true, true);

				if (!string.IsNullOrEmpty(token))
				{
					token = token.ToLowerInvariant();

					// SOC state. Keep the small amount of deterministic state data that
					// affects how a placed object looks in the editor.
					if(token == "state")
					{
						SkipWhitespace(true, true);
						string statename = ReadToken(true, true);
						float previewscale;
						if(TryReadStatePreviewScale(out previewscale))
							statepreviewscales[statename] = previewscale;
						else
							statepreviewscales.Remove(statename);
					}
					// SOC object
					else if (token.StartsWith("thing") || token.EndsWith("mobj") || token.EndsWith("object"))
					{
						SkipWhitespace(true, true);
						token = ReadToken(true, true);

						if (!token.ToUpper().StartsWith("MT_"))
							continue;

						string objname = token;

						// Read actor structure
						ActorStructure mobj = new SOCMobjStructure(this, objname);
						if (this.HasError) return false;

						mobjs[mobj.DoomEdNum] = mobj;
					}
					// Level header
					else if (token.Equals("level"))
					{
						SkipWhitespace(true, true);
						token = ReadToken(true, true);

						string mapname = GetMapName(token);

						if (mapname == null)
							continue;

						SOCLevelHeader levelheader = new SOCLevelHeader(this);
						if (this.HasError) return false;

						mapheaders[mapname.ToLowerInvariant()] = levelheader.MapInfo;
					}
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
			mapheaders = new Dictionary<string, MapInfo>();
			statepreviewscales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			parsedlumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase); //mxd
		}

		private bool TryReadStatePreviewScale(out float scale)
		{
			scale = 1.0f;
			string action = string.Empty;
			string var1 = string.Empty;
			string var2 = "0";

			// State headers are followed by a newline. SOC definitions end at the
			// next empty line, just like object definitions.
			ReadLine();
			while(SkipWhitespace(false, true))
			{
				string name = ReadToken();
				if(name == "\n") break;

				SkipWhitespace(false, true);
				if(ReadToken() != "=")
				{
					ReadLine();
					continue;
				}

				SkipWhitespace(true, true);
				string value = ReadLine();
				int comment = value.IndexOf('#');
				if(comment >= 0) value = value.Substring(0, comment);
				value = value.Trim().TrimEnd(',');

				switch(name.ToLowerInvariant())
				{
					case "action": action = value; break;
					case "var1": var1 = value; break;
					case "var2": var2 = value; break;
				}
			}

			// A_SetScale can target another object through Var2. Only preview the
			// deterministic self-targeting form; runtime targets cannot be known here.
			int options;
			if(!action.Equals("A_SetScale", StringComparison.OrdinalIgnoreCase)
				|| !TryEvaluateInteger(var2, out options)
				|| (options >> 16) != 0)
				return false;

			return TryEvaluateFixedScale(var1, out scale) && scale > 0.0f;
		}

		private void ApplyStatePreviewScales()
		{
			foreach(ActorStructure actor in mobjs.Values)
			{
				actor.Properties.Remove("$actionscale");
				string spawnstate = actor.GetPropertyValueString("spawnstate", 0).Trim();
				float scale;
				if(string.IsNullOrEmpty(spawnstate) || !RunsSpawnAction(actor)
					|| !statepreviewscales.TryGetValue(spawnstate, out scale))
					continue;

				// Keep state action scale separate from the object's configured sprite
				// scale. The renderer composes both values exactly once.
				string value = scale.ToString(CultureInfo.InvariantCulture);
				actor.Properties["$actionscale"] = new List<string> { value };
			}
		}

		internal static bool RunsSpawnAction(ActorStructure actor)
		{
			string flags = actor.GetPropertyAllValues("flags");
			return flags.IndexOf("MF_RUNSPAWNFUNC", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal static bool TryEvaluateFixedScale(string expression, out float scale)
		{
			scale = 0.0f;
			if(string.IsNullOrWhiteSpace(expression)) return false;

			string value = expression.Replace(" ", string.Empty).ToUpperInvariant();
			const double fracunit = 65536.0;
			double result;

			if(value.Contains("FRACUNIT"))
			{
				value = value.Replace("FRACUNIT", "1");
				if(!TryEvaluateProduct(value, out result)) return false;
			}
			else
			{
				if(!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return false;
				result /= fracunit;
			}

			if(double.IsNaN(result) || double.IsInfinity(result) || result <= 0.0 || result > float.MaxValue)
				return false;

			scale = (float)result;
			return true;
		}

		internal static bool TryEvaluateInteger(string expression, out int result)
		{
			result = 0;
			if(string.IsNullOrWhiteSpace(expression)) return true;

			double value;
			if(!TryEvaluateProduct(expression.Replace(" ", string.Empty), out value)
				|| value < int.MinValue || value > int.MaxValue)
				return false;

			result = (int)value;
			return Math.Abs(value - result) < 0.0000001;
		}

		// Evaluate the simple constant multiplication/division expressions used by
		// SOC state Var fields. Deliberately reject names and runtime expressions.
		private static bool TryEvaluateProduct(string expression, out double result)
		{
			result = 0.0;
			if(string.IsNullOrWhiteSpace(expression)) return false;

			int index = 0;
			double current;
			if(!ReadNumber(expression, ref index, out current)) return false;

			while(index < expression.Length)
			{
				char operation = expression[index++];
				if(operation != '*' && operation != '/') return false;

				double operand;
				if(!ReadNumber(expression, ref index, out operand) || (operation == '/' && operand == 0.0))
					return false;

				current = operation == '*' ? current * operand : current / operand;
			}

			result = current;
			return true;
		}

		private static bool ReadNumber(string expression, ref int index, out double value)
		{
			value = 0.0;
			int start = index;
			if(index < expression.Length && (expression[index] == '+' || expression[index] == '-')) index++;
			while(index < expression.Length && (char.IsDigit(expression[index]) || expression[index] == '.')) index++;
			if(start == index) return false;

			return double.TryParse(expression.Substring(start, index - start), NumberStyles.Float,
				CultureInfo.InvariantCulture, out value);
		}

		private string GetMapName(string number)
		{
			if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
				return ConvertToExtendedMapNum(n);
			else
			{
				if (number.Length != 2 || number[0] < 'A' || number[0] > 'Z' || !((number[1] >= '0' && number[1] <= '9') || (number[1] >= 'A' && number[1] <= 'Z')))
				{
					//ReportError("Invalid level number");
					return null;
				}
				return "MAP" + number;
			}

		}
		private string ConvertToExtendedMapNum(int n)
		{
			if (n <= 0 || n > 1035)
			{
				//ReportError("Invalid level number");
				return null;
			}
			if (n < 10)
				return "MAP0" + n;
			if (n < 100)
				return "MAP" + n.ToString();

			int x = n - 100;
			int p = x / 36;
			int q = x % 36;
			char a = (char)('A' + p);
			char b = (q < 10) ? (char)('0' + q) : (char)('A' + q - 10);
			return "MAP" + String.Concat(a, b);
		}

		#endregion
	}
}
