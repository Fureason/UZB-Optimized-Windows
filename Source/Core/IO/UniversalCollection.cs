
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

using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	public sealed class UniversalCollection : List<UniversalEntry>
	{
		// Variables
		private string comment;
		private readonly Dictionary<string, UniversalEntry> entriesbykey =
			new Dictionary<string, UniversalEntry>(System.StringComparer.Ordinal);
		
		// Properties
		public string Comment { get { return comment; } set { comment = value; } }

		// Overload
		public void Add(string key, object value)
		{
			Add(new UniversalEntry(key, value));
		}

		// UniversalStreamReader requests many managed fields from every UDMF block.
		// Keep the latest entry by name so those reads do not rescan the complete list.
		// UDMF's existing duplicate behavior is retained: the last value wins.
		public new void Add(UniversalEntry entry)
		{
			base.Add(entry);
			if(entry != null) entriesbykey[entry.Key] = entry;
		}

		public bool TryGetEntry(string key, out UniversalEntry entry)
		{
			return entriesbykey.TryGetValue(key, out entry);
		}
	}
}
