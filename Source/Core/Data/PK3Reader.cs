
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
using System.Security.Cryptography;
using System.Text;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

#endregion

namespace CodeImp.DoomBuilder.Data
{
	internal sealed class PK3Reader : PK3StructuredReader
	{
		private const int RESOURCE_INDEX_CACHE_VERSION = 1;
		private const string RESOURCE_INDEX_CACHE_MAGIC = "UZBPK3INDEX";
		private const int REUSABLE_FILE_CACHE_MAX_ITEM_BYTES = 2 * 1024 * 1024;
		private const long REUSABLE_FILE_CACHE_MAX_BYTES = 64L * 1024L * 1024L;

		// Definition files are read repeatedly when maps are reopened or when resources
		// are reloaded. Decompressing the same DECORATE/SOC/Lua/TEXTURES data each time
		// wastes a noticeable part of a warm load. Keep a small process-wide LRU of the
		// decompressed bytes. The archive path, size and timestamp are part of the key,
		// so replacing a resource automatically makes every old entry unreachable.
		private sealed class ReusableFileCacheEntry
		{
			internal readonly byte[] Data;
			internal readonly LinkedListNode<string> Node;

			internal ReusableFileCacheEntry(byte[] data, LinkedListNode<string> node)
			{
				Data = data;
				Node = node;
			}
		}

		private static readonly object reusablefilecachelock = new object();
		private static readonly Dictionary<string, ReusableFileCacheEntry> reusablefilecache =
			new Dictionary<string, ReusableFileCacheEntry>(StringComparer.OrdinalIgnoreCase);
		private static readonly LinkedList<string> reusablefilecachelru = new LinkedList<string>();
		private static long reusablefilecachebytes;

		#region ================== Variables

		private /*readonly*/ DirectoryFilesList files;
		private IArchive archive; //mxd
		private Dictionary<string, IArchiveEntry> archiveentries;
		private /*readonly*/ ArchiveType archivetype; //mxd
		private /*readonly*/ Dictionary<string, byte[]> sevenzipentries; //mxd
		private bool batchmode = true; //mxd
		private FileStream filestream;
		private string reusablefilecacheprefix;
		private int reusablefilecachehits;
		private int reusablefilecachemisses;

		#endregion

		#region ================== Properties (mxd)

		public bool BatchMode
        {
            get
            {
                return batchmode;
            }

            set
            {
                if (batchmode != value)
                {
                    batchmode = value;
                    UpdateArchive(batchmode);
                }
            }
        }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public PK3Reader(DataLocation dl, GameConfiguration config, bool asreadonly) : base(dl, asreadonly)
		{
            LoadFrom(dl, config, asreadonly);
		}

        private void LoadFrom(DataLocation dl, GameConfiguration config, bool asreadonly)
        {
			FileAccess access;
			FileShare share;

			isreadonly = asreadonly;

			// Determine if opening for read only
			if (isreadonly)
			{
				// Read only
				access = FileAccess.Read;
				share = FileShare.ReadWrite;
			}
			else
			{
				// Private access
				access = FileAccess.ReadWrite;
				share = FileShare.Read;
			}

			General.WriteLogLine("Opening " + Path.GetExtension(location.location).ToUpper().Replace(".", "") + " resource \"" + location.location + "\"");

            if (!File.Exists(location.location))
                throw new FileNotFoundException("Could not find the file \"" + location.location + "\"", location.location);

			FileInfo sourceinfo = new FileInfo(location.location);
			reusablefilecacheprefix = Path.GetFullPath(location.location).ToUpperInvariant() + "|"
				+ sourceinfo.Length.ToString() + "|" + sourceinfo.LastWriteTimeUtc.Ticks.ToString() + "|";

			// Opening large PK3 central directories accounted for a substantial part of
			// repeated map loads. Cache the unfiltered entry list on disk and invalidate it
			// using the source file's size and last-write timestamp. File contents are still
			// read from the archive, so the cache cannot make textures or definitions stale.
			List<DirectoryFileEntry> fileentries;
			if(TryLoadResourceIndexCache(out fileentries))
			{
				archivetype = ArchiveType.Zip;
				General.WriteLogLine("Resource index cache hit: \"" + location.location + "\" (" + fileentries.Count + " entries)");
			}
			else
			{
				fileentries = new List<DirectoryFileEntry>();

				// Take the detour with a FileStream because SharpCompress doesn't directly support opening files as read-only
				filestream = File.Open(location.location, FileMode.OpenOrCreate, access, share);

				// Create archive
				archive = ArchiveFactory.Open(filestream);
				archivetype = archive.Type;

				// Random access of 7z archives works TERRIBLY slow in SharpCompress
				if (archivetype == ArchiveType.SevenZip)
				{
					isreadonly = true; // Unsaveable...
					sevenzipentries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

					IReader reader = archive.ExtractAllEntries();
					while (reader.MoveToNextEntry())
					{
						if (reader.Entry.IsDirectory || !CheckInvalidPathChars(reader.Entry.Key)) continue;

						MemoryStream s = new MemoryStream();
						reader.WriteEntryTo(s);
						sevenzipentries.Add(reader.Entry.Key.ToLowerInvariant(), s.ToArray());
						fileentries.Add(new DirectoryFileEntry(reader.Entry.Key));
					}
				}
				else
				{
					BuildArchiveEntryLookup(fileentries);
				}

				// SevenZip data has already been extracted into memory. ZIP-family
				// archives remain open in batch mode so Initialize can reuse both the
				// parsed central directory and lookup instead of reopening and enumerating
				// the same archive on the first image or definition.
				if(archivetype == ArchiveType.SevenZip)
				{
					archive.Dispose();
					archive = null;
					filestream.Dispose();
					filestream = null;
				}

				if(archivetype == ArchiveType.Zip) SaveResourceIndexCache(fileentries);
			}

            // Make files list
            files = new DirectoryFilesList(dl.GetDisplayName(), config, Silent, fileentries);

            // Initialize without path (because we use paths relative to the PK3 file)
            Initialize(config);

            // We have no destructor
            GC.SuppressFinalize(this);
        }

		private bool CanUseResourceIndexCache()
		{
			string extension = Path.GetExtension(location.location);
			return isreadonly && (string.Equals(extension, ".pk3", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase));
		}

		private string GetResourceIndexCachePath()
		{
			byte[] pathbytes = Encoding.UTF8.GetBytes(Path.GetFullPath(location.location).ToUpperInvariant());
			byte[] hash;
			using(SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(pathbytes);
			string filename = BitConverter.ToString(hash).Replace("-", string.Empty) + ".idx";
			return Path.Combine(General.SettingsPath, "ResourceIndexCache", filename);
		}

		private bool TryLoadResourceIndexCache(out List<DirectoryFileEntry> fileentries)
		{
			fileentries = null;
			if(!CanUseResourceIndexCache()) return false;

			try
			{
				FileInfo source = new FileInfo(location.location);
				string cachepath = GetResourceIndexCachePath();
				if(!File.Exists(cachepath)) return false;

				using(BinaryReader reader = new BinaryReader(File.Open(cachepath, FileMode.Open, FileAccess.Read, FileShare.Read), Encoding.UTF8))
				{
					if(reader.ReadString() != RESOURCE_INDEX_CACHE_MAGIC) return false;
					if(reader.ReadInt32() != RESOURCE_INDEX_CACHE_VERSION) return false;
					if(reader.ReadInt64() != source.Length || reader.ReadInt64() != source.LastWriteTimeUtc.Ticks) return false;

					int count = reader.ReadInt32();
					if(count < 0 || count > 2000000) return false;
					fileentries = new List<DirectoryFileEntry>(count);
					for(int i = 0; i < count; i++) fileentries.Add(new DirectoryFileEntry(reader.ReadString()));
				}

				return true;
			}
			catch(Exception)
			{
				fileentries = null;
				return false;
			}
		}

		private void SaveResourceIndexCache(ICollection<DirectoryFileEntry> fileentries)
		{
			if(!CanUseResourceIndexCache()) return;

			try
			{
				FileInfo source = new FileInfo(location.location);
				string cachepath = GetResourceIndexCachePath();
				Directory.CreateDirectory(Path.GetDirectoryName(cachepath));
				using(BinaryWriter writer = new BinaryWriter(File.Open(cachepath, FileMode.Create, FileAccess.Write, FileShare.None), Encoding.UTF8))
				{
					writer.Write(RESOURCE_INDEX_CACHE_MAGIC);
					writer.Write(RESOURCE_INDEX_CACHE_VERSION);
					writer.Write(source.Length);
					writer.Write(source.LastWriteTimeUtc.Ticks);
					writer.Write(fileentries.Count);
					foreach(DirectoryFileEntry entry in fileentries) writer.Write(entry.filepathname);
				}
			}
			catch(Exception)
			{
				// The cache is optional. Read-only folders and antivirus locks must not
				// prevent a resource from loading normally.
			}
		}

		// Disposer
		public override void Dispose()
		{
			// Not already disposed?
			if(!isdisposed)
			{
				General.WriteLogLine("Closing " + Path.GetExtension(location.location).ToUpper().Replace(".", "") + " resource \"" + location.location + "\"");
				if(reusablefilecachehits > 0 || reusablefilecachemisses > 0)
					General.WriteLogLine("Reusable definition cache for \"" + location.GetDisplayName() + "\": "
						+ reusablefilecachehits + " hits, " + reusablefilecachemisses + " misses.");

				//mxd. Remove temp files
				foreach(WADReader wr in wads)
				{
					try { File.Delete(wr.Location.location); }
					catch(Exception) { }
				}

				//mxd
				if(archive != null)
				{
					archive.Dispose();
					archive = null;
					archiveentries = null;
				}

				if(filestream != null)
				{
					filestream.Dispose();
					filestream = null;
				}

				// Done
				base.Dispose();
			}
		}

		//mxd
		private void UpdateArchive(bool enable) 
		{
			lock (this)
			{
				if (archivetype == ArchiveType.SevenZip) return;

				if (enable && archive == null)
				{
					FileAccess access;
					FileShare share;

					// Determine if opening for read only
					if (isreadonly)
					{
						// Read only
						access = FileAccess.Read;
						share = FileShare.ReadWrite;
					}
					else
					{
						// Private access
						access = FileAccess.ReadWrite;
						share = FileShare.Read;
					}

					// The file might have vanished in the meantime
					if (!File.Exists(location.location))
						throw new FileNotFoundException("Could not find the file \"" + location.location + "\"", location.location);

					// Take the detour with a FileStream because SharpCompress doesn't directly support opening files as read-only
					filestream = File.Open(location.location, FileMode.OpenOrCreate, access, share);

					archive = ArchiveFactory.Open(filestream);

					// SharpCompress exposes archive entries as an enumerable. Looking up every
					// texture by scanning that enumerable made loading N images from a large
					// PK3 approach O(N * archive entries). Build one case-insensitive lookup
					// for the lifetime of this open archive instead.
					BuildArchiveEntryLookup(null);
				}
				else if (!enable && !batchmode && archive != null)
				{
					archive.Dispose();
					archive = null;
					archiveentries = null;

					filestream.Dispose();
					filestream = null;
				}
			}
		}

		private void BuildArchiveEntryLookup(List<DirectoryFileEntry> fileentries)
		{
			archiveentries = new Dictionary<string, IArchiveEntry>(StringComparer.OrdinalIgnoreCase);
			foreach(IArchiveEntry entry in archive.Entries)
			{
				if(entry.IsDirectory || !CheckInvalidPathChars(entry.Key)) continue;
				string entryname = entry.Key.Replace('\\', '/');
				if(!archiveentries.ContainsKey(entryname)) archiveentries.Add(entryname, entry);
				if(fileentries != null) fileentries.Add(new DirectoryFileEntry(entry.Key));
			}
		}

        #endregion

        #region ================== Textures

        // This finds and returns a patch stream
        public override Stream GetPatchData(string pname, bool longname, ref string patchlocation)
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			// Note the backward order, because the last wad's images have priority
			if(!longname) //mxd. Patches with long names can't be in wads
			{
				for(int i = wads.Count - 1; i >= 0; i--)
				{
					Stream data = wads[i].GetPatchData(pname, false, ref patchlocation);
					if(data != null) return data;
				}
			}
			else
			{
				//mxd. Long names are absolute
				if(FileExists(pname))
				{
					patchlocation = location.GetDisplayName();
					return LoadFile(pname);
				}
				return null;
			}

			if(General.Map.Config.MixTexturesFlats)
			{
				//mxd. Find in directories ZDoom expects them to be
				foreach(string loc in PatchLocations)
				{
					string filename = FindFirstFile(loc, pname, true);
					if((filename != null) && FileExists(filename))
					{
						patchlocation = location.GetDisplayName();
						return LoadFile(filename);
					}
				}
			}
			else
			{
				// Find in patches directory
				string filename = FindFirstFile(PATCHES_DIR, pname, true);
				if((filename != null) && FileExists(filename))
				{
					patchlocation = location.GetDisplayName();
					return LoadFile(filename);
				}
			}

			// Nothing found
			return null;
		}

		// This finds and returns a textue stream
		public override Stream GetTextureData(string pname, bool longname, ref string texturelocation)
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			// Note the backward order, because the last wad's images have priority
			if(!longname) //mxd. Textures with long names can't be in wads
			{
				for(int i = wads.Count - 1; i >= 0; i--)
				{
					Stream data = wads[i].GetTextureData(pname, false, ref texturelocation);
					if(data != null) return data;
				}
			}
			else
			{
				//mxd. Long names are absolute
				if(FileExists(pname))
				{
					texturelocation = location.GetDisplayName();
					return LoadFile(pname);
				}
				return null;
			}

			// Find in textures directory
			string filename = FindFirstFile(TEXTURES_DIR, pname, true);
			if(!string.IsNullOrEmpty(filename) && FileExists(filename))
			{
				texturelocation = location.GetDisplayName();
				return LoadFile(filename);
			}

			// Nothing found
			return null;
		}

		//mxd. This finds and returns a HiRes textue stream
		public override Stream GetHiResTextureData(string name, ref string hireslocation)
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			// Note the backward order, because the last wad's images have priority
			for(int i = wads.Count - 1; i >= 0; i--)
			{
				Stream data = wads[i].GetTextureData(name, false, ref hireslocation);
				if(data != null) return data;
			}

			// Find in HiRes directory
			string filename = FindFirstFile(HIRES_DIR, name, true);
			if(!string.IsNullOrEmpty(filename) && FileExists(filename))
			{
				hireslocation = location.GetDisplayName();
				return LoadFile(filename);
			}

			// Nothing found
			return null;
		}

		// This finds and returns a colormap stream
		public override Stream GetColormapData(string pname)
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			// Note the backward order, because the last wad's images have priority
			for(int i = wads.Count - 1; i >= 0; i--)
			{
				Stream data = wads[i].GetColormapData(pname);
				if(data != null) return data;
			}

			// Find in patches directory
			string filename = FindFirstFile(COLORMAPS_DIR, pname, true);
			if((filename != null) && FileExists(filename))
			{
				return LoadFile(filename);
			}

			// Nothing found
			return null;
		}

		#endregion

		#region ================== Sprites

		// This finds and returns a sprite stream
		public override Stream GetSpriteData(string pname, ref string spritelocation)
		{
			string pfilename = pname.Replace('\\', '^');

			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			for(int i = wads.Count - 1; i >= 0; i--)
			{
				Stream sprite = wads[i].GetSpriteData(pname, ref spritelocation);
				if(sprite != null) return sprite;
			}

			// Find in sprites directory
			string filename = FindFirstFile(SPRITES_DIR, pfilename, true);
			if((filename != null) && FileExists(filename))
			{
				spritelocation = location.GetDisplayName(); //mxd
				return LoadFile(filename);
			}

			// Nothing found
			return null;
		}

		// This checks if the given sprite exists
		public override bool GetSpriteExists(string pname)
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			string pfilename = pname.Replace('\\', '^');

			// Find in any of the wad files
			for(int i = wads.Count - 1; i >= 0; i--)
			{
				if(wads[i].GetSpriteExists(pname)) return true;
			}

			// Find in sprites directory
			string filename = FindFirstFile(SPRITES_DIR, pfilename, true);
			if((filename != null) && FileExists(filename))
			{
				return true;
			}

			// Nothing found
			return false;
		}

		#endregion

		#region ================== Voxels (mxd)

		//mxd. This finds and returns a voxel stream or null if no voxel was found
		public override Stream GetVoxelData(string name, ref string voxellocation) 
		{
			// Error when suspended
			if(issuspended) throw new Exception("Data reader is suspended");

			// Find in any of the wad files
			for(int i = wads.Count - 1; i >= 0; i--) 
			{
				Stream voxel = wads[i].GetVoxelData(name, ref voxellocation);
				if(voxel != null) return voxel;
			}

			name = name.Replace('\\', '^');

			try
			{
				// Find in voxels directory
				string filename = null;

				// There are different places we have to look for the file, depending on how it was defined. Right now
				// there's no way to know if the definition comes from VOXELDEF or spreite replacement, so we're assuming
				// it's from VOXELDEF if there's an extension given.
				// - Auto-detect for sprite name: must be in the "voxels" directory
				// - Just given as file name without path in VOXELDEF: must be in the root directory
				// - Given as full path and file name in VOXELDEF: must be in exactly that spot
				string path = Path.GetDirectoryName(name);
				if (string.IsNullOrWhiteSpace(path))
				{
					if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
						filename = FindFirstFile(VOXELS_DIR, Path.GetFileName(name), true);
					else
						filename = FindFirstFileWithExt("", Path.GetFileName(name), true);
				}
				else
				{
					filename = FindFirstFileWithExt(path, Path.GetFileName(name), false);
				}

				if ((filename != null) && FileExists(filename))
				{
					voxellocation = location.GetDisplayName();
					return LoadFile(filename);
				}
			}
			catch (Exception e)
			{
				if (!Silent) General.ErrorLogger.Add(ErrorType.Error, e.GetType().Name + " while loading voxel \"" + name + "\" from PK3: " + e.Message);
			}

			// Nothing found
			return null;
		}

		#endregion
		
		#region ================== Methods

		// Return a short name for this data location
		public override string GetTitle()
		{
			return Path.GetFileName(location.location);
		}

		// This creates an image
		protected override ImageData CreateImage(string filename, int imagetype)
		{
			switch(imagetype)
			{
				case ImageDataFormat.DOOMFLAT:
					return new PK3FileImage(this, filename, true);

				case ImageDataFormat.DOOMPICTURE:
					return new PK3FileImage(this, filename, false);

				case ImageDataFormat.DOOMCOLORMAP:
					return new ColormapImage(Path.GetFileNameWithoutExtension(filename));
					
				default:
					throw new ArgumentException("Invalid image format specified!");
			}
		}

		// This returns true if the specified file exists
		internal override bool FileExists(string filename, int unused) { return files.FileExists(filename); }
		internal override bool FileExists(string filename)
		{
			return files.FileExists(filename);
		}

		// This returns all files in a given directory
		protected override string[] GetAllFiles(string path, bool subfolders)
		{
			return files.GetAllFiles(path, subfolders).ToArray();
		}

		//mxd. This returns wad files in the root directory
		protected override string[] GetWadFiles()
		{
			return files.GetWadFiles().ToArray();
		}
		
		// This returns all files in a given directory that have the given title
		protected override string[] GetAllFilesWithTitle(string path, string title, bool subfolders)
		{
			return files.GetAllFilesWithTitle(path, title, subfolders).ToArray();
		}

		//mxd. This returns all files in a given directory which title starts with given title
		protected override string[] GetAllFilesWhichTitleStartsWith(string path, string title, bool subfolders) 
		{
			return files.GetAllFilesWhichTitleStartsWith(path, title, subfolders).ToArray();
		}
		
		// This returns all files in a given directory that match the given extension
		internal override string[] GetFilesWithExt(string path, string extension, bool subfolders)
		{
			return files.GetAllFiles(path, extension, subfolders).ToArray();
		}

		// This finds the first file that has the specific name, regardless of file extension
		internal override string FindFirstFile(string beginswith, bool subfolders)
		{
			return files.GetFirstFile(beginswith, subfolders);
		}

		// This finds the first file that has the specific name, regardless of file extension
		protected override string FindFirstFile(string path, string beginswith, bool subfolders)
		{
			return files.GetFirstFile(path, beginswith, subfolders);
		}

		// This finds the first file that has the specific name
		protected override string FindFirstFileWithExt(string path, string beginswith, bool subfolders)
		{
			string title = Path.GetFileNameWithoutExtension(beginswith);
			string ext = Path.GetExtension(beginswith);
			ext = (!string.IsNullOrEmpty(ext) && ext.Length > 1 ? ext.Substring(1) : "");
			return files.GetFirstFile(path, title, subfolders, ext);
		}

		// This loads an entire file in memory and returns the stream
		// NOTE: Callers are responsible for disposing the stream!
		internal override MemoryStream LoadFile(string filename)
		{
			MemoryStream filedata = null;
			string fn = filename.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); //mxd
			string cachekey = null;
			if(isreadonly && IsReusableDefinitionFile(fn))
			{
				cachekey = reusablefilecacheprefix + fn;
				byte[] cacheddata;
				if(TryGetReusableFile(cachekey, out cacheddata))
				{
					reusablefilecachehits++;
					return new MemoryStream(cacheddata, 0, cacheddata.Length, false, true);
				}
				reusablefilecachemisses++;
			}

			//mxd. This works waaaaaay faster with 7z archive
			if(archivetype == ArchiveType.SevenZip)
			{
				fn = fn.ToLowerInvariant();
				if(sevenzipentries.ContainsKey(fn)) filedata = new MemoryStream(sevenzipentries[fn]);
			} 
			else 
			{
				lock (this)
				{
					UpdateArchive(true);

					IArchiveEntry entry;
					if(archiveentries != null && archiveentries.TryGetValue(fn, out entry))
					{
						filedata = new MemoryStream();

						try
						{
							entry.WriteTo(filedata);
						}
						catch(SharpCompress.Compressors.Deflate.ZlibException)
						{
							// This happens when the PK3 was modified externally and the resources were not reloaded
							if (!Silent) General.ErrorLogger.Add(ErrorType.Error, "Cannot load the file \"" + filename + "\" from archive \"" + location.GetDisplayName() + "\". Did you modify the archive without reloading the resouces?");

							filedata.Dispose();
							filedata = null;

							return null;
						}
						catch(NotSupportedException e)
						{
							if (!Silent) General.ErrorLogger.Add(ErrorType.Error, "Cannot load the file \"" + filename + "\" from archive \"" + location.GetDisplayName() + "\". " + e.Message);

							filedata.Dispose();
							filedata = null;

							return null;
						}
					}

					UpdateArchive(false);
				}
			}
			
			// Nothing found?
			if(filedata == null)
			{
				//mxd
				if (!Silent) General.ErrorLogger.Add(ErrorType.Error, "Cannot find the file \"" + filename + "\" in archive \"" + location.GetDisplayName() + "\".");
				return null;
			}

			filedata.Position = 0; //mxd. rewind before use
			if(cachekey != null && filedata.Length <= REUSABLE_FILE_CACHE_MAX_ITEM_BYTES)
			{
				byte[] data = filedata.ToArray();
				StoreReusableFile(cachekey, data);
				filedata.Dispose();
				return new MemoryStream(data, 0, data.Length, false, true);
			}
			return filedata;
		}

		private static bool IsReusableDefinitionFile(string filename)
		{
			string extension = Path.GetExtension(filename).ToUpperInvariant();
			if(extension == ".SOC" || extension == ".LUA" || extension == ".TXT"
				|| extension == ".CFG" || extension == ".ZS" || extension == ".DEC") return true;

			string title = Path.GetFileNameWithoutExtension(filename).ToUpperInvariant();
			return title == "DECORATE" || title == "ZSCRIPT" || title == "TEXTURES"
				|| title == "MAPINFO" || title == "ZMAPINFO" || title == "MODELDEF"
				|| title == "GLDEFS" || title == "DEHACKED" || title == "CVARINFO"
				|| title == "LOCKDEFS" || title == "DECALDEF" || title == "SNDINFO"
				|| title == "SNDSEQ" || title == "REVERBS" || title == "ANIMDEFS"
				|| title == "TERRAIN" || title == "VOXELDEF" || title == "PNAMES";
		}

		private static bool TryGetReusableFile(string key, out byte[] data)
		{
			lock(reusablefilecachelock)
			{
				ReusableFileCacheEntry entry;
				if(!reusablefilecache.TryGetValue(key, out entry))
				{
					data = null;
					return false;
				}

				reusablefilecachelru.Remove(entry.Node);
				reusablefilecachelru.AddFirst(entry.Node);
				data = entry.Data;
				return true;
			}
		}

		private static void StoreReusableFile(string key, byte[] data)
		{
			if(data == null || data.Length == 0 || data.Length > REUSABLE_FILE_CACHE_MAX_ITEM_BYTES) return;

			lock(reusablefilecachelock)
			{
				ReusableFileCacheEntry previous;
				if(reusablefilecache.TryGetValue(key, out previous))
				{
					reusablefilecachelru.Remove(previous.Node);
					reusablefilecachebytes -= previous.Data.Length;
					reusablefilecache.Remove(key);
				}

				LinkedListNode<string> node = reusablefilecachelru.AddFirst(key);
				reusablefilecache.Add(key, new ReusableFileCacheEntry(data, node));
				reusablefilecachebytes += data.Length;

				while(reusablefilecachebytes > REUSABLE_FILE_CACHE_MAX_BYTES && reusablefilecachelru.Last != null)
				{
					string oldestkey = reusablefilecachelru.Last.Value;
					ReusableFileCacheEntry oldest = reusablefilecache[oldestkey];
					reusablefilecachebytes -= oldest.Data.Length;
					reusablefilecache.Remove(oldestkey);
					reusablefilecachelru.RemoveLast();
				}
			}
		}

		//mxd
		internal override bool SaveFile(MemoryStream stream, string filename, int unused) { return SaveFile(stream, filename); }
		internal override bool SaveFile(MemoryStream stream, string filename)
		{
			// Not implemented in SevenZipArchive...
			if(isreadonly || archivetype == ArchiveType.SevenZip) return false;

			// Convert slashes...
			filename = filename.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			// Check if target file is locked...
			var checkresult = FileLockChecker.CheckFile(location.location);
			if(!string.IsNullOrEmpty(checkresult.Error))
			{
				string errmsg = "Unable to save file \"" + filename + "\" into archive \"" + location.GetDisplayName() + "\".";
				if(checkresult.Processes.Count > 0)
				{
					string processpath = string.Empty;
					try
					{
						// All manner of exceptions are possible here...
						processpath = checkresult.Processes[0].MainModule.FileName;
					}
					catch { }

					errmsg += " Archive is locked by " + checkresult.Processes[0].ProcessName
						+ " (" + (!string.IsNullOrEmpty(processpath) ? "\"" + processpath + "\"" : "")
						+ ", started at " + checkresult.Processes[0].StartTime + ").";
				}
				
				if (!Silent) General.ErrorLogger.Add(ErrorType.Error, errmsg);
				return false;
			}

			using(MemoryStream savestream = new MemoryStream())
			{
				using(ZipArchive za = (ZipArchive)ArchiveFactory.Open(location.location))
				{
					if(za == null)
					{
						string errmsg = "Unable to save file \"" + filename + "\" into archive \"" + location.GetDisplayName() + "\". Unable to open target file as a zip archive.";
						if (!Silent) General.ErrorLogger.Add(ErrorType.Error, errmsg);
						return false;
					}

					// Find and remove original entry...
					foreach(ZipArchiveEntry entry in za.Entries)
					{
						if(!entry.IsDirectory && entry.Key == filename)
						{
							za.RemoveEntry(entry);
							break;
						}
					}

					// Add new entry and save the archive to stream...
					za.AddEntry(filename, stream, 0L, DateTime.Now);
					za.SaveTo(savestream, CompressionType.Deflate);
				}

				// Replace archive file...
				File.WriteAllBytes(location.location, savestream.ToArray());
			}

			// Rewind the stream (because DirectoryReader/WADReader don't modify stream Position in SaveFile)
			stream.Position = 0;

			return true;
		}

		// This creates a temp file for the speciied file and return the absolute path to the temp file
		// NOTE: Callers are responsible for removing the temp file when done!
		protected override string CreateTempFile(string filename)
		{
			// Just copy the file
			string tempfile = General.MakeTempFilename(General.Map.TempPath, "wad");
			MemoryStream filedata = LoadFile(filename);
			File.WriteAllBytes(tempfile, filedata.ToArray());
			filedata.Dispose();
			return tempfile;
		}

		//mxd. This replicates System.IO.Path.CheckInvalidPathChars() internal function
		private bool CheckInvalidPathChars(string path)
		{
			foreach(char c in path)
			{
				int num = c;
				switch(num)
				{
					case 34:
					case 60:
					case 62:
					case 124:
						if (!Silent) General.ErrorLogger.Add(ErrorType.Error, "Error in \"" + location.GetDisplayName() + "\": unsupported character \"" + c + "\" in path \"" + path + "\". File loading was skipped.");
						return false;

					default:
						if(num >= 32) continue;
						else goto case 34;
				}
			}

			return true;
		}

		#endregion

		#region ================== Compiling (mxd)

		// This compiles a script lump and returns any errors that may have occurred
		// Returns true when our code worked properly (even when the compiler returned errors)
		internal override bool CompileLump(string filename, int unused, ScriptConfiguration scriptconfig, List<CompilerError> errors) { return CompileLump(filename, scriptconfig, errors); }
		internal override bool CompileLump(string filename, ScriptConfiguration scriptconfig, List<CompilerError> errors)
		{
			// No compiling required
			if(scriptconfig.Compiler == null) return true;

			// Initialize compiler
			Compiler compiler;
			try
			{
				compiler = scriptconfig.Compiler.Create();
			}
			catch(Exception e)
			{
				// Fail
				errors.Add(new CompilerError("Unable to initialize compiler. " + e.GetType().Name + ": " + e.Message));
				return false;
			}

			// Extract the source file into the temporary directory
			string inputfile = Path.Combine(compiler.Location, Path.GetFileName(filename));
			using(MemoryStream stream = LoadFile(filename))
			{
				File.WriteAllBytes(inputfile, stream.ToArray());
			}

			// Make random output filename
			string outputfile = General.MakeTempFilename(compiler.Location, "tmp");

			// Run compiler
			compiler.Parameters = scriptconfig.Parameters;
			compiler.InputFile = inputfile;
			compiler.OutputFile = Path.GetFileName(outputfile);
			compiler.SourceFile = inputfile;
			compiler.WorkingDirectory = Path.GetDirectoryName(inputfile);
			if(compiler.Run())
			{
				// Fetch errors
				foreach(CompilerError e in compiler.Errors)
				{
					CompilerError newerr = e;

					// If the error's filename equals our temporary file, // replace it with the original source filename
					if(String.Compare(e.filename, inputfile, true) == 0) newerr.filename = filename;

					errors.Add(newerr);
				}

				// No errors and output file exists?
				if(compiler.Errors.Length == 0)
				{
					// Output file exists?
					if(!File.Exists(outputfile))
					{
						// Fail
						compiler.Dispose();
						errors.Add(new CompilerError("Output file \"" + outputfile + "\" doesn't exist."));
						return false;
					}

					//mxd. Move and rename the result file
					string targetfilename;
					if (compiler is AccCompiler acccompiler)
					{
						targetfilename = Path.Combine(Path.GetDirectoryName(filename), acccompiler.Parser.LibraryName + ".o");
					}
					else
					{
						//mxd. No can't do...
						if (String.IsNullOrEmpty(scriptconfig.ResultLump))
						{
							// Fail
							compiler.Dispose();
							errors.Add(new CompilerError("Unable to create target file: unable to determine target filename. Make sure \"ResultLump\" property is set in the \"" + scriptconfig + "\" script configuration."));
							return false;
						}

						targetfilename = Path.Combine(Path.GetDirectoryName(filename), scriptconfig.ResultLump);
					}

					// Rename and add to source archive
					try
					{
						byte[] buffer = File.ReadAllBytes(outputfile);
						using(MemoryStream stream = new MemoryStream(buffer.Length))
						{
							stream.Write(buffer, 0, buffer.Length);
							SaveFile(stream, targetfilename);
						}
					}
					catch(Exception e)
					{
						// Fail
						compiler.Dispose();
						errors.Add(new CompilerError("Unable to create library file \"" + targetfilename + "\". " + e.GetType().Name + ": " + e.Message));
						return false;
					}
				}

				// Done
				compiler.Dispose();
				return true;
			}

			// Fail
			compiler.Dispose();
			return false;
		}

		#endregion
	}
}
