
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
using System.IO;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	/// <summary>
	/// Compares two paths, ignoring case and converting the secondary string to the system's directory separator
	/// </summary>
	internal sealed class PathEqualityComparer : IEqualityComparer<string>
	{
		public bool Equals(string s1, string s2)
		{
			if(ReferenceEquals(s1, s2)) return true;
			if(s1 == null || s2 == null || s1.Length != s2.Length) return false;

			// Resource paths can use either slash regardless of the host platform. Compare
			// them without allocating normalized/lowercase copies for every dictionary hit.
			for(int i = 0; i < s1.Length; i++)
			{
				char c1 = NormalizeSeparator(s1[i]);
				char c2 = NormalizeSeparator(s2[i]);
				if(char.ToUpperInvariant(c1) != char.ToUpperInvariant(c2)) return false;
			}

			return true;
		}

		public int GetHashCode(string s)
		{
			if(s == null) return 0;

			unchecked
			{
				int hash = 5381;
				for(int i = 0; i < s.Length; i++)
					hash = ((hash << 5) + hash) ^ char.ToUpperInvariant(NormalizeSeparator(s[i]));
				return hash;
			}
		}

		private static char NormalizeSeparator(char c)
		{
			return (c == '\\' || c == '/') ? Path.DirectorySeparatorChar : c;
		}
	}

	internal sealed class DirectoryFilesList
	{
		#region ================== Constants (mxd)

		#endregion

		#region ================== Variables

		private Dictionary<string, DirectoryFileEntry> entries; //mxd
		private List<string> wadentries; //mxd
		private List<DirectoryFileEntry> orderedentries;
		private Dictionary<string, List<DirectoryFileEntry>> filesbypath;
		private Dictionary<string, List<DirectoryFileEntry>> filesbypathrecursive;
		private Dictionary<string, List<DirectoryFileEntry>> filesbytitle;
		
		#endregion

		#region ================== Properties

		public int Count { get { return entries.Count; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor to fill list from directory and optionally subdirectories
		public DirectoryFilesList(string path, GameConfiguration config, bool subdirectories)
		{
			path = Path.GetFullPath(path);
			string[] files = Directory.GetFiles(path, "*", subdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			Array.Sort(files); //mxd
			entries = new Dictionary<string, DirectoryFileEntry>(files.Length, new PathEqualityComparer());
			wadentries = new List<string>();
			
			foreach(string file in files) //mxd
			{
				var e = new DirectoryFileEntry(file, path);
				if(string.Compare(e.extension, "wad", true) == 0 && e.path.Length == 0)
				{
					wadentries.Add(file);
					continue;
				}

				if (config != null)
				{
					if (config.IgnoredFileExtensions.Contains(e.extension)) continue;

					bool skipfolder = false;
					foreach (string ef in config.IgnoredDirectoryNames)
					{
						if (e.path.StartsWith(ef + Path.DirectorySeparatorChar))
						{
							skipfolder = true;
							break;
						}
					}
					if (skipfolder) continue;
				}

				AddOrReplaceEntry(e);
			}

			BuildIndexes();
		}

		// Constructor for custom list
		public DirectoryFilesList(string resourcename, GameConfiguration config, bool silent, ICollection<DirectoryFileEntry> sourceentries)
		{
			entries = new Dictionary<string, DirectoryFileEntry>(sourceentries.Count, new PathEqualityComparer());
			wadentries = new List<string>();
			int duplicatecount = 0;
			List<string> duplicateexamples = new List<string>(8);
			foreach(DirectoryFileEntry e in sourceentries)
			{
				if(string.Compare(e.extension, "wad", true) == 0 && e.path.Length == 0)
				{
					wadentries.Add(e.filepathname);
					continue;
				}

				if (config != null)
				{
					if (config.IgnoredFileExtensions.Contains(e.extension)) continue;

					bool skipfolder = false;
					foreach (string ef in config.IgnoredDirectoryNames)
					{
						if (e.path.StartsWith(ef + Path.DirectorySeparatorChar))
						{
							skipfolder = true;
							break;
						}
					}
					if (skipfolder) continue;
				}

				DirectoryFileEntry existingentry;
				if(entries.TryGetValue(e.filepathname, out existingentry))
				{
					duplicatecount++;
					if(duplicateexamples.Count < duplicateexamples.Capacity) duplicateexamples.Add(e.filepathname);
					continue;
				}

				AddOrReplaceEntry(e);
			}

			// Some add-ons contain hundreds of case-duplicate entries. Logging each one
			// separately made resource loading and the Errors window unnecessarily slow.
			if(!silent && duplicatecount > 0)
			{
				string examples = (duplicateexamples.Count > 0 ? " Examples: \"" + string.Join("\", \"", duplicateexamples.ToArray()) + "\"." : string.Empty);
				General.ErrorLogger.Add(ErrorType.Warning, "Resource \"" + resourcename + "\" contains " + duplicatecount
					+ " duplicate file name" + (duplicatecount == 1 ? string.Empty : "s") + ". The first entry for each name was used." + examples);
			}

			BuildIndexes();
		}

		#endregion

		#region ================== Methods

		// This checks whether a file is in the entry dictionary, adds it if it
		// isn't, and replaces the existing entry if the new entry is lowercase
		private void AddOrReplaceEntry(DirectoryFileEntry e)
		{
			// If the entry is already in the dictionary, add the one with
			// greater ordinal value (prefer lowercase)
			DirectoryFileEntry existingentry;
			if(entries.TryGetValue(e.filepathname, out existingentry))
			{
				// Get the key for the existing entry. It may have a
				// different case, since the dictionary is set up to be
				// case insensitive.
				string existingEntryPath = existingentry.filepathname;
				if(e.filepathname.CompareTo(existingEntryPath) == -1)
				{
					entries[e.filepathname] = e;
				}
			}
			else
			{
				// Just add the entry, since it's not in the dictionary yet.
				entries.Add(e.filepathname, e);
			}
		}

		private void BuildIndexes()
		{
			PathEqualityComparer pathcomparer = new PathEqualityComparer();
			orderedentries = new List<DirectoryFileEntry>(entries.Count);
			filesbypath = new Dictionary<string, List<DirectoryFileEntry>>(pathcomparer);
			filesbypathrecursive = new Dictionary<string, List<DirectoryFileEntry>>(pathcomparer);
			filesbytitle = new Dictionary<string, List<DirectoryFileEntry>>(StringComparer.OrdinalIgnoreCase);

			foreach(DirectoryFileEntry e in entries.Values)
			{
				orderedentries.Add(e);
				AddIndexedEntry(filesbypath, e.path, e);
				AddIndexedEntry(filesbypathrecursive, string.Empty, e);

				// Add the entry to each containing directory. This turns the many full
				// resource scans used by texture/definition discovery into direct lookups.
				if(e.path.Length > 0)
				{
					int separatorindex = 0;
					while((separatorindex = e.path.IndexOf(Path.DirectorySeparatorChar, separatorindex)) != -1)
					{
						AddIndexedEntry(filesbypathrecursive, e.path.Substring(0, separatorindex + 1), e);
						separatorindex++;
					}
				}

				List<DirectoryFileEntry> titleentries;
				if(!filesbytitle.TryGetValue(e.filetitle, out titleentries))
				{
					titleentries = new List<DirectoryFileEntry>();
					filesbytitle.Add(e.filetitle, titleentries);
				}
				titleentries.Add(e);
			}
		}

		private static void AddIndexedEntry(Dictionary<string, List<DirectoryFileEntry>> index, string key, DirectoryFileEntry entry)
		{
			List<DirectoryFileEntry> list;
			if(!index.TryGetValue(key, out list))
			{
				list = new List<DirectoryFileEntry>();
				index.Add(key, list);
			}
			list.Add(entry);
		}

		private static List<string> GetFileNames(List<DirectoryFileEntry> source)
		{
			if(source == null) return new List<string>();
			List<string> result = new List<string>(source.Count);
			foreach(DirectoryFileEntry e in source) result.Add(e.filepathname);
			return result;
		}

		private List<DirectoryFileEntry> GetEntriesForPath(string path, bool subdirectories)
		{
			path = NormalizeQueryPath(path);
			List<DirectoryFileEntry> result;
			Dictionary<string, List<DirectoryFileEntry>> index = (subdirectories ? filesbypathrecursive : filesbypath);
			return index.TryGetValue(path, out result) ? result : null;
		}

		// This checks if a given file exists
		// The given file path must not be absolute
		public bool FileExists(string filepathname)
		{
			return entries.ContainsKey(filepathname);
		}

		// This returns file information for the given file
		// The given file path must not be absolute
		public DirectoryFileEntry GetFileInfo(string filepathname)
		{
			return entries[filepathname];
		}

		//mxd. This returns a list of all wad files (filepathname)
		public List<string> GetWadFiles()
		{
			return wadentries;
		}
		
		// This returns a list of all files (filepathname)
		public List<string> GetAllFiles()
		{
			return GetFileNames(orderedentries);
		}

		// This returns a list of all files optionally with subdirectories included
		public List<string> GetAllFiles(bool subdirectories)
		{
			if(subdirectories) return GetAllFiles();

			return GetFileNames(GetEntriesForPath(string.Empty, false));
		}

		// This returns a list of all files that are in the given path and optionally in subdirectories
		public List<string> GetAllFiles(string path, bool subdirectories)
		{
			return GetFileNames(GetEntriesForPath(path, subdirectories));
		}

		// This returns a list of all files that are in the given path and subdirectories and have the given title
		public List<string> GetAllFilesWithTitle(string path, string title)
		{
			path = NormalizeQueryPath(path);
			List<DirectoryFileEntry> candidates;
			List<string> files = new List<string>();
			if(!filesbytitle.TryGetValue(title, out candidates)) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(e.path.StartsWith(path, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path (optionally in subdirectories) and have the given title
		public List<string> GetAllFilesWithTitle(string path, string title, bool subdirectories)
		{
			if(subdirectories) return GetAllFilesWithTitle(path, title);

			path = NormalizeQueryPath(path);
			List<DirectoryFileEntry> candidates;
			List<string> files = new List<string>();
			if(!filesbytitle.TryGetValue(title, out candidates)) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(string.Equals(e.path, path, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		//mxd. This returns a list of all files that are in the given path and which names starts with title
		public List<string> GetAllFilesWhichTitleStartsWith(string path, string title)
		{
			path = NormalizeQueryPath(path);
			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, true);
			List<string> files = new List<string>();
			if(candidates == null) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(e.filetitle.StartsWith(title, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		//mxd. This returns a list of all files that are in the given path and which names starts with title
		public List<string> GetAllFilesWhichTitleStartsWith(string path, string title, bool subdirectories) 
		{
			if(subdirectories) return GetAllFilesWhichTitleStartsWith(path, title);

			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, false);
			List<string> files = new List<string>();
			if(candidates == null) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(e.filetitle.StartsWith(title, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path and subdirectories and have the given extension
		public List<string> GetAllFiles(string path, string extension)
		{
			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, true);
			List<string> files = new List<string>();
			if(candidates == null) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(string.Equals(e.extension, extension, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path (optionally in subdirectories) and have the given extension
		public List<string> GetAllFiles(string path, string extension, bool subdirectories)
		{
			if(subdirectories) return GetAllFiles(path, extension);

			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, false);
			List<string> files = new List<string>();
			if(candidates == null) return files;
			foreach(DirectoryFileEntry e in candidates)
				if(string.Equals(e.extension, extension, StringComparison.OrdinalIgnoreCase)) files.Add(e.filepathname);
			return files;
		}

		// This finds the first file that has the specified name, regardless of file extension
		public string GetFirstFile(string title, bool subdirectories)
		{
			if(subdirectories)
			{
				List<DirectoryFileEntry> candidates;
				if(filesbytitle.TryGetValue(title, out candidates) && candidates.Count > 0) return candidates[0].filepathname;
			}
			else
			{
				List<DirectoryFileEntry> candidates = GetEntriesForPath(string.Empty, false);
				if(candidates != null)
					foreach(DirectoryFileEntry e in candidates)
						if(string.Equals(e.filetitle, title, StringComparison.OrdinalIgnoreCase)) return e.filepathname;
			}

			return null;
		}

		// This finds the first file that has the specified name and extension
		public string GetFirstFile(string title, bool subdirectories, string extension)
		{
			List<DirectoryFileEntry> candidates;
			if(subdirectories)
				filesbytitle.TryGetValue(title, out candidates);
			else
				candidates = GetEntriesForPath(string.Empty, false);
			if(candidates != null)
				foreach(DirectoryFileEntry e in candidates)
					if(string.Equals(e.filetitle, title, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(e.extension, extension, StringComparison.OrdinalIgnoreCase)) return e.filepathname;

			return null;
		}

		// This finds the first file that has the specified name, regardless of file extension, and is in the given path
		public string GetFirstFile(string path, string title, bool subdirectories)
		{
			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, subdirectories);
			if(candidates != null)
				foreach(DirectoryFileEntry e in candidates)
					if(e.filetitle.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH
						? e.filetitle.StartsWith(title, StringComparison.OrdinalIgnoreCase)
						: string.Equals(e.filetitle, title, StringComparison.OrdinalIgnoreCase)) return e.filepathname;

			return null;
		}

		// This finds the first file that has the specified name, is in the given path and has the given extension
		public string GetFirstFile(string path, string title, bool subdirectories, string extension)
		{
			List<DirectoryFileEntry> candidates = GetEntriesForPath(path, subdirectories);
			if(candidates != null)
				foreach(DirectoryFileEntry e in candidates)
					if(string.Equals(e.filetitle, title, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(e.extension, extension, StringComparison.OrdinalIgnoreCase)) return e.filepathname;

			return null;
		}
		
		// This fixes a path so that it ends with the proper directory separator (mxd)
		private static string CorrectPath(string path)
		{
			if(path.Length > 0)
			{
				if(path[path.Length - 1] == Path.DirectorySeparatorChar) return path;
				if(path[path.Length - 1] == Path.AltDirectorySeparatorChar)
					path = path.Substring(0, path.Length - 1);
				return path + Path.DirectorySeparatorChar;
			}

			return path;
		}

		private static string NormalizeQueryPath(string path)
		{
			if(string.IsNullOrEmpty(path)) return string.Empty;
			if(Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
				path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return CorrectPath(path);
		}

		#endregion
	}
}
