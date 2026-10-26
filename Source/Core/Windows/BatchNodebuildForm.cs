#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Properties;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	/// <summary>
	/// Rebuilds nodes for selected maps in one or more WADs without opening each
	/// map in the editor. Files are staged, checked, and replaced atomically.
	/// </summary>
	internal sealed class BatchNodebuildForm : DelayedForm
	{
		#region ================== Variables

		private readonly ListView mapslist;
		private readonly Button addfiles;
		private readonly Button removefiles;
		private readonly Button selectall;
		private readonly Button selectnone;
		private readonly Button rebuild;
		private readonly Button cancel;
		private readonly Button close;
		private readonly CheckBox keepbackups;
		private readonly Label selectionsummary;
		private readonly Label progresslabel;
		private readonly ProgressBar progressbar;
		private readonly RichTextBox log;
		private readonly BackgroundWorker worker;
		private readonly Dictionary<string, ListViewItem> mapitems;
		private readonly BatchNodebuildTool binarytool;
		private readonly BatchNodebuildTool udmftool;

		#endregion

		#region ================== Constructor

		internal BatchNodebuildForm()
		{
			Text = "Batch Rebuild Nodes";
			Icon = Resources.UZB;
			ClientSize = new Size(900, 610);
			MinimumSize = new Size(720, 500);
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = true;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			Opacity = 0.0;
			AllowDrop = true;

			mapitems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
			binarytool = FindSafeNodebuilder(false);
			udmftool = FindSafeNodebuilder(true);

			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Padding = new Padding(10);
			layout.ColumnCount = 1;
			layout.RowCount = 7;
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

			Label explanation = new Label();
			explanation.Dock = DockStyle.Fill;
			explanation.BackColor = SystemColors.Info;
			explanation.ForeColor = SystemColors.InfoText;
			explanation.BorderStyle = BorderStyle.FixedSingle;
			explanation.Padding = new Padding(8, 6, 8, 6);
			explanation.Text = "Choose one or more WADs, check the maps to process, then rebuild them together. "
				+ "This uses the safe Normal node profiles. Unrelated WAD data and every unselected map are verified before a file is replaced.";
			layout.Controls.Add(explanation, 0, 0);

			FlowLayoutPanel toolbar = new FlowLayoutPanel();
			toolbar.Dock = DockStyle.Fill;
			toolbar.FlowDirection = FlowDirection.LeftToRight;
			toolbar.WrapContents = false;
			toolbar.Padding = new Padding(0, 5, 0, 0);

			addfiles = new Button();
			addfiles.AutoSize = true;
			addfiles.Text = "Add WAD files...";
			addfiles.Click += addfiles_Click;
			toolbar.Controls.Add(addfiles);

			removefiles = new Button();
			removefiles.AutoSize = true;
			removefiles.Text = "Remove selected";
			removefiles.Enabled = false;
			removefiles.Click += removefiles_Click;
			toolbar.Controls.Add(removefiles);

			selectall = new Button();
			selectall.AutoSize = true;
			selectall.Text = "Check all";
			selectall.Enabled = false;
			selectall.Click += selectall_Click;
			toolbar.Controls.Add(selectall);

			selectnone = new Button();
			selectnone.AutoSize = true;
			selectnone.Text = "Check none";
			selectnone.Enabled = false;
			selectnone.Click += selectnone_Click;
			toolbar.Controls.Add(selectnone);

			selectionsummary = new Label();
			selectionsummary.AutoSize = true;
			selectionsummary.Margin = new Padding(14, 6, 0, 0);
			selectionsummary.Text = "No maps added";
			toolbar.Controls.Add(selectionsummary);
			layout.Controls.Add(toolbar, 0, 1);

			mapslist = new ListView();
			mapslist.Dock = DockStyle.Fill;
			mapslist.View = View.Details;
			mapslist.CheckBoxes = true;
			mapslist.FullRowSelect = true;
			mapslist.GridLines = true;
			mapslist.HideSelection = false;
			mapslist.ShowItemToolTips = true;
			mapslist.Columns.Add("Map", 90);
			mapslist.Columns.Add("Format", 90);
			mapslist.Columns.Add("WAD", 190);
			mapslist.Columns.Add("Folder", 330);
			mapslist.Columns.Add("Status", 170);
			mapslist.ItemCheck += mapslist_ItemCheck;
			mapslist.SelectedIndexChanged += mapslist_SelectedIndexChanged;
			mapslist.KeyDown += mapslist_KeyDown;
			layout.Controls.Add(mapslist, 0, 2);

			Panel safety = new Panel();
			safety.Dock = DockStyle.Fill;
			keepbackups = new CheckBox();
			keepbackups.AutoSize = true;
			keepbackups.Checked = true;
			keepbackups.Location = new Point(0, 8);
			keepbackups.Text = "Keep a timestamped backup beside each changed WAD (recommended)";
			safety.Controls.Add(keepbackups);
			layout.Controls.Add(safety, 0, 3);

			TableLayoutPanel progresslayout = new TableLayoutPanel();
			progresslayout.Dock = DockStyle.Fill;
			progresslayout.ColumnCount = 2;
			progresslayout.RowCount = 1;
			progresslayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
			progresslayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			progresslabel = new Label();
			progresslabel.Dock = DockStyle.Fill;
			progresslabel.TextAlign = ContentAlignment.MiddleLeft;
			progresslabel.Text = "Ready";
			progressbar = new ProgressBar();
			progressbar.Dock = DockStyle.Fill;
			progressbar.Style = ProgressBarStyle.Continuous;
			progresslayout.Controls.Add(progresslabel, 0, 0);
			progresslayout.Controls.Add(progressbar, 1, 0);
			layout.Controls.Add(progresslayout, 0, 4);

			log = new RichTextBox();
			log.Dock = DockStyle.Fill;
			log.ReadOnly = true;
			log.BackColor = SystemColors.Window;
			log.DetectUrls = false;
			log.WordWrap = true;
			log.Text = "Add WAD files or drag them onto this window. A WAD currently open in UZB is skipped so unsaved editor changes can never be overwritten.";
			layout.Controls.Add(log, 0, 5);

			FlowLayoutPanel buttons = new FlowLayoutPanel();
			buttons.Dock = DockStyle.Fill;
			buttons.FlowDirection = FlowDirection.RightToLeft;
			buttons.WrapContents = false;
			buttons.Padding = new Padding(0, 7, 0, 0);

			close = new Button();
			close.AutoSize = true;
			close.Text = "Close";
			close.DialogResult = DialogResult.Cancel;
			buttons.Controls.Add(close);

			cancel = new Button();
			cancel.AutoSize = true;
			cancel.Text = "Cancel batch";
			cancel.Enabled = false;
			cancel.Click += cancel_Click;
			buttons.Controls.Add(cancel);

			rebuild = new Button();
			rebuild.AutoSize = true;
			rebuild.Text = "Rebuild selected nodes";
			rebuild.Enabled = false;
			rebuild.Click += rebuild_Click;
			buttons.Controls.Add(rebuild);
			layout.Controls.Add(buttons, 0, 6);

			Controls.Add(layout);
			CancelButton = close;
			AcceptButton = rebuild;

			ToolTip tips = new ToolTip();
			tips.SetToolTip(addfiles, "Add several WAD files at once and discover the maps inside them.");
			tips.SetToolTip(keepbackups, "The original file is copied to a .bak file before the verified node build replaces it.");
			tips.SetToolTip(rebuild, "Rebuild only the checked maps. Maps that are not checked are left byte-for-byte unchanged.");

			worker = new BackgroundWorker();
			worker.WorkerReportsProgress = true;
			worker.WorkerSupportsCancellation = true;
			worker.DoWork += worker_DoWork;
			worker.ProgressChanged += worker_ProgressChanged;
			worker.RunWorkerCompleted += worker_RunWorkerCompleted;

			DragEnter += BatchNodebuildForm_DragEnter;
			DragDrop += BatchNodebuildForm_DragDrop;
		}

		#endregion

		#region ================== Interface

		private static BatchNodebuildTool FindSafeNodebuilder(bool udmf)
		{
			bool unix = (Environment.OSVersion.Platform == PlatformID.Unix);
			string name = (unix ? "zdbsp-linux_" : "zdbsp_") + (udmf ? "udmf_normal" : "normal");
			NodebuilderInfo info = General.GetNodebuilderByName(name);
			if(info == null) return null;

			string parameters = info.Parameters;
			// Classic nodes may need generated split vertices, but pruning unused
			// sidedefs/sectors is unrelated to rebuilding and would be surprising in
			// a batch maintenance tool.
			if(!udmf && parameters.IndexOf("-q", StringComparison.Ordinal) == -1)
				parameters = "-q " + parameters;

			return new BatchNodebuildTool(Path.Combine(info.Compiler.Path, info.Compiler.ProgramFile), parameters);
		}

		private void addfiles_Click(object sender, EventArgs e)
		{
			using(OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Add WAD files for batch node rebuilding";
				dialog.Filter = "WAD archives (*.wad)|*.wad|All files (*.*)|*.*";
				dialog.Multiselect = true;
				dialog.CheckFileExists = true;
				if(dialog.ShowDialog(this) == DialogResult.OK) AddWadFiles(dialog.FileNames);
			}
		}

		private void AddWadFiles(IEnumerable<string> filenames)
		{
			foreach(string filename in filenames)
			{
				try
				{
					string fullpath = Path.GetFullPath(filename);
					if(!File.Exists(fullpath))
					{
						AppendLog("Skipped missing file: " + fullpath);
						continue;
					}

					if(General.Map != null && !string.IsNullOrEmpty(General.Map.FilePathName)
						&& string.Equals(Path.GetFullPath(General.Map.FilePathName), fullpath, StringComparison.OrdinalIgnoreCase))
					{
						AppendLog("Skipped " + Path.GetFileName(fullpath) + ": it is currently open in UZB. Save and close it before using the batch builder.");
						continue;
					}

					List<BatchNodebuildMap> maps = BatchNodebuildEngine.DiscoverMaps(fullpath, true);
					if(maps.Count == 0)
					{
						AppendLog("No maps were found in " + fullpath);
						continue;
					}

					ListViewGroup group = mapslist.Groups.Cast<ListViewGroup>()
						.FirstOrDefault(g => string.Equals(g.Name, fullpath, StringComparison.OrdinalIgnoreCase));
					if(group == null)
					{
						group = new ListViewGroup(fullpath, Path.GetFileName(fullpath));
						mapslist.Groups.Add(group);
					}

					foreach(BatchNodebuildMap map in maps)
					{
						if(mapitems.ContainsKey(map.Key)) continue;
						ListViewItem item = new ListViewItem(map.MapName);
						item.SubItems.Add(map.IsUdmf ? "UDMF" : "Binary");
						item.SubItems.Add(Path.GetFileName(map.FilePath));
						item.SubItems.Add(Path.GetDirectoryName(map.FilePath));
						item.SubItems.Add("Ready");
						item.Tag = map;
						item.ToolTipText = map.FilePath + " | " + map.MapName;
						item.Group = group;
						mapslist.Items.Add(item);
						mapitems.Add(map.Key, item);
						item.Checked = true;
					}

					AppendLog("Added " + maps.Count + " map" + (maps.Count == 1 ? "" : "s") + " from " + fullpath);
				}
				catch(Exception ex)
				{
					AppendLog("Could not add " + filename + ": " + ex.Message);
				}
			}

			UpdateSelectionSummary();
		}

		private void removefiles_Click(object sender, EventArgs e)
		{
			List<ListViewItem> selected = mapslist.SelectedItems.Cast<ListViewItem>().ToList();
			foreach(ListViewItem item in selected)
			{
				BatchNodebuildMap map = item.Tag as BatchNodebuildMap;
				if(map != null) mapitems.Remove(map.Key);
				mapslist.Items.Remove(item);
			}

			foreach(ListViewGroup group in mapslist.Groups.Cast<ListViewGroup>().ToList())
				if(group.Items.Count == 0) mapslist.Groups.Remove(group);

			UpdateSelectionSummary();
		}

		private void selectall_Click(object sender, EventArgs e)
		{
			foreach(ListViewItem item in mapslist.Items) item.Checked = true;
			UpdateSelectionSummary();
		}

		private void selectnone_Click(object sender, EventArgs e)
		{
			foreach(ListViewItem item in mapslist.Items) item.Checked = false;
			UpdateSelectionSummary();
		}

		private void mapslist_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			BeginInvoke((MethodInvoker)UpdateSelectionSummary);
		}

		private void mapslist_SelectedIndexChanged(object sender, EventArgs e)
		{
			removefiles.Enabled = !worker.IsBusy && mapslist.SelectedItems.Count > 0;
		}

		private void mapslist_KeyDown(object sender, KeyEventArgs e)
		{
			if(!e.Control || e.KeyCode != Keys.A || worker.IsBusy) return;

			mapslist.BeginUpdate();
			try
			{
				foreach(ListViewItem item in mapslist.Items) item.Selected = true;
				if(mapslist.Items.Count > 0) mapslist.Items[0].Focused = true;
			}
			finally
			{
				mapslist.EndUpdate();
			}

			e.Handled = true;
			e.SuppressKeyPress = true;
		}

		private void UpdateSelectionSummary()
		{
			int selectedmaps = mapslist.CheckedItems.Count;
			int selectedfiles = mapslist.CheckedItems.Cast<ListViewItem>()
				.Select(i => ((BatchNodebuildMap)i.Tag).FilePath)
				.Distinct(StringComparer.OrdinalIgnoreCase).Count();

			selectionsummary.Text = (mapslist.Items.Count == 0
				? "No maps added"
				: selectedmaps + " of " + mapslist.Items.Count + " maps checked in " + selectedfiles + " WAD" + (selectedfiles == 1 ? "" : "s"));
			rebuild.Enabled = !worker.IsBusy && selectedmaps > 0 && binarytool != null && udmftool != null;
			selectall.Enabled = !worker.IsBusy && mapslist.Items.Count > 0;
			selectnone.Enabled = !worker.IsBusy && mapslist.Items.Count > 0;
			removefiles.Enabled = !worker.IsBusy && mapslist.SelectedItems.Count > 0;
		}

		private void rebuild_Click(object sender, EventArgs e)
		{
			List<BatchNodebuildMap> maps = mapslist.CheckedItems.Cast<ListViewItem>()
				.Select(i => (BatchNodebuildMap)i.Tag).ToList();
			if(maps.Count == 0) return;

			if(binarytool == null || udmftool == null)
			{
				AppendLog("The safe ZDBSP Normal profiles could not be found. Check the Compilers\\Nodebuilders installation.");
				return;
			}

			foreach(ListViewItem item in mapslist.CheckedItems) item.SubItems[4].Text = "Waiting";
			SetBusy(true);
			progressbar.Value = 0;
			progresslabel.Text = "Starting batch...";
			AppendLog("Starting a safe batch rebuild for " + maps.Count + " selected map" + (maps.Count == 1 ? "" : "s") + ".");
			worker.RunWorkerAsync(new BatchNodebuildRequest(maps, keepbackups.Checked, binarytool, udmftool));
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			if(!worker.IsBusy) return;
			worker.CancelAsync();
			cancel.Enabled = false;
			progresslabel.Text = "Cancel requested; finishing the current map safely...";
			AppendLog("Cancel requested. The original WAD remains untouched until the current file has fully passed verification.");
		}

		private void SetBusy(bool busy)
		{
			addfiles.Enabled = !busy;
			mapslist.Enabled = !busy;
			keepbackups.Enabled = !busy;
			close.Enabled = !busy;
			cancel.Enabled = busy;
			selectall.Enabled = !busy && mapslist.Items.Count > 0;
			selectnone.Enabled = !busy && mapslist.Items.Count > 0;
			removefiles.Enabled = !busy && mapslist.SelectedItems.Count > 0;
			rebuild.Enabled = !busy && mapslist.CheckedItems.Count > 0;
		}

		private void worker_DoWork(object sender, DoWorkEventArgs e)
		{
			try
			{
				e.Result = BatchNodebuildEngine.Run((BatchNodebuildRequest)e.Argument, worker);
			}
			catch(Exception ex)
			{
				e.Result = BatchNodebuildResult.FromFatalError(ex);
			}
		}

		private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			progressbar.Value = Math.Max(progressbar.Minimum, Math.Min(progressbar.Maximum, e.ProgressPercentage));
			BatchNodebuildProgress progress = e.UserState as BatchNodebuildProgress;
			if(progress == null) return;

			if(!string.IsNullOrEmpty(progress.StatusText)) progresslabel.Text = progress.StatusText;
			if(!string.IsNullOrEmpty(progress.LogText)) AppendLog(progress.LogText);
			if(!string.IsNullOrEmpty(progress.MapKey) && mapitems.ContainsKey(progress.MapKey))
				mapitems[progress.MapKey].SubItems[4].Text = progress.MapStatus;
		}

		private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			SetBusy(false);
			BatchNodebuildResult result = e.Result as BatchNodebuildResult;
			if(result == null)
			{
				progresslabel.Text = "Batch failed";
				AppendLog("The batch ended unexpectedly. No unverified staging file was installed.");
			}
			else
			{
				progressbar.Value = result.Canceled ? progressbar.Value : 100;
				progresslabel.Text = result.Summary;
				AppendLog(result.Summary);
			}

			UpdateSelectionSummary();
		}

		private void AppendLog(string message)
		{
			if(string.IsNullOrEmpty(message)) return;
			if(log.TextLength > 0) log.AppendText(Environment.NewLine);
			log.AppendText(message);
			log.SelectionStart = log.TextLength;
			log.ScrollToCaret();
		}

		private void BatchNodebuildForm_DragEnter(object sender, DragEventArgs e)
		{
			if(!worker.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
			else e.Effect = DragDropEffects.None;
		}

		private void BatchNodebuildForm_DragDrop(object sender, DragEventArgs e)
		{
			string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
			if(files != null) AddWadFiles(files.Where(f => string.Equals(Path.GetExtension(f), ".wad", StringComparison.OrdinalIgnoreCase)));
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if(worker != null && worker.IsBusy)
			{
				worker.CancelAsync();
				e.Cancel = true;
				progresslabel.Text = "Cancel requested; finishing the current map safely...";
				return;
			}

			base.OnFormClosing(e);
		}

		#endregion
	}

	#region ================== Batch node builder

	internal sealed class BatchNodebuildMap
	{
		internal readonly string FilePath;
		internal readonly string MapName;
		internal readonly bool IsUdmf;
		internal string Key { get { return FilePath + "\0" + MapName; } }

		internal BatchNodebuildMap(string filepath, string mapname, bool isudmf)
		{
			FilePath = filepath;
			MapName = mapname;
			IsUdmf = isudmf;
		}
	}

	internal sealed class BatchNodebuildTool
	{
		internal readonly string Program;
		internal readonly string Parameters;

		internal BatchNodebuildTool(string program, string parameters)
		{
			Program = program;
			Parameters = parameters;
		}
	}

	internal sealed class BatchNodebuildRequest
	{
		internal readonly List<BatchNodebuildMap> Maps;
		internal readonly bool KeepBackups;
		internal readonly BatchNodebuildTool BinaryTool;
		internal readonly BatchNodebuildTool UdmfTool;

		internal BatchNodebuildRequest(List<BatchNodebuildMap> maps, bool keepbackups,
			BatchNodebuildTool binarytool, BatchNodebuildTool udmftool)
		{
			Maps = maps;
			KeepBackups = keepbackups;
			BinaryTool = binarytool;
			UdmfTool = udmftool;
		}
	}

	internal sealed class BatchNodebuildProgress
	{
		internal string StatusText;
		internal string LogText;
		internal string MapKey;
		internal string MapStatus;
	}

	internal sealed class BatchNodebuildResult
	{
		internal int FilesUpdated;
		internal int FilesUnchanged;
		internal int FilesFailed;
		internal int MapsCompleted;
		internal bool Canceled;
		internal string FatalError;

		internal string Summary
		{
			get
			{
				if(!string.IsNullOrEmpty(FatalError)) return "Batch failed: " + FatalError;
				if(Canceled) return "Batch canceled safely. Completed " + MapsCompleted + " map" + (MapsCompleted == 1 ? "" : "s") + "; no partial WAD was installed.";
				return "Batch finished: " + MapsCompleted + " map" + (MapsCompleted == 1 ? "" : "s")
					+ ", " + FilesUpdated + " WAD" + (FilesUpdated == 1 ? "" : "s") + " updated"
					+ (FilesUnchanged > 0 ? ", " + FilesUnchanged + " already current" : "")
					+ (FilesFailed > 0 ? ", " + FilesFailed + " failed (originals untouched)" : "") + ".";
			}
		}

		internal static BatchNodebuildResult FromFatalError(Exception ex)
		{
			return new BatchNodebuildResult { FatalError = ex.GetType().Name + ": " + ex.Message, FilesFailed = 1 };
		}
	}

	internal static class BatchNodebuildEngine
	{
		internal static readonly HashSet<string> BINARY_MAP_LUMPS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS",
			"REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS", "DIALOGUE", "ZNODES", "LEAFS", "LIGHTS", "MACROS"
		};

		private static readonly HashSet<string> NODE_LUMPS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"SEGS", "SSECTORS", "NODES", "ZNODES", "REJECT", "BLOCKMAP",
			"GL_VERT", "GL_SEGS", "GL_SSECT", "GL_NODES", "GL_PVS"
		};

		internal static List<BatchNodebuildMap> DiscoverMaps(string filepath, bool rejectiwad)
		{
			string fullpath = Path.GetFullPath(filepath);
			using(WAD wad = new WAD(fullpath, true))
			{
				if(rejectiwad && wad.IsIWAD)
					throw new InvalidOperationException("IWAD files are read-only game data and cannot be batch rebuilt.");

				List<MapRange> ranges = DiscoverMapRanges(wad);
				if(ranges.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
					throw new InvalidOperationException("This WAD contains duplicate map header names, which ZDBSP cannot select safely with -m.");

				return ranges.Select(r => new BatchNodebuildMap(fullpath, r.Name, r.IsUdmf)).ToList();
			}
		}

		internal static BatchNodebuildResult Run(BatchNodebuildRequest request, BackgroundWorker worker)
		{
			BatchNodebuildResult result = new BatchNodebuildResult();
			List<IGrouping<string, BatchNodebuildMap>> files = request.Maps
				.GroupBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
			int totalmaps = request.Maps.Count;
			int attemptedmaps = 0;

			foreach(IGrouping<string, BatchNodebuildMap> filegroup in files)
			{
				List<BatchNodebuildMap> maps = filegroup.ToList();
				if(IsCancellationPending(worker))
				{
					result.Canceled = true;
					break;
				}

				bool changed;
				string backup;
				string error;
				bool canceled;
				bool success = ProcessFile(filegroup.Key, maps, request, worker, attemptedmaps, totalmaps,
					out changed, out backup, out error, out canceled);

				if(canceled)
				{
					result.Canceled = true;
					ReportMaps(worker, attemptedmaps, totalmaps, maps, "Canceled", "Canceled " + Path.GetFileName(filegroup.Key) + "; original untouched.");
					break;
				}

				attemptedmaps += maps.Count;
				if(success)
				{
					result.MapsCompleted += maps.Count;
					if(changed) result.FilesUpdated++; else result.FilesUnchanged++;
					string status = (changed ? "Rebuilt" : "Already current");
					string message = (changed
						? "Updated " + Path.GetFileName(filegroup.Key) + (string.IsNullOrEmpty(backup) ? "." : "; backup: " + backup)
						: Path.GetFileName(filegroup.Key) + " already had identical node data; no file was replaced.");
					ReportMaps(worker, attemptedmaps, totalmaps, maps, status, message);
				}
				else
				{
					result.FilesFailed++;
					ReportMaps(worker, attemptedmaps, totalmaps, maps, "Failed; original safe",
						"Could not update " + Path.GetFileName(filegroup.Key) + ": " + error + " The original WAD was not changed.");
				}
			}

			return result;
		}

		private static bool ProcessFile(string filepath, List<BatchNodebuildMap> maps,
			BatchNodebuildRequest request, BackgroundWorker worker, int completedbefore, int totalmaps,
			out bool changed, out string backuppath, out string error, out bool canceled)
		{
			changed = false;
			backuppath = string.Empty;
			error = string.Empty;
			canceled = false;
			string stagingdirectory = string.Empty;

			try
			{
				if(!File.Exists(filepath)) throw new FileNotFoundException("The WAD no longer exists.", filepath);
				if((File.GetAttributes(filepath) & FileAttributes.ReadOnly) != 0)
					throw new InvalidOperationException("The WAD is marked read-only.");

				List<BatchNodebuildMap> discovered = DiscoverMaps(filepath, true);
				foreach(BatchNodebuildMap map in maps)
				{
					BatchNodebuildMap current = discovered.FirstOrDefault(m => string.Equals(m.MapName, map.MapName, StringComparison.OrdinalIgnoreCase));
					if(current == null) throw new InvalidOperationException("Map " + map.MapName + " is no longer present.");
					if(current.IsUdmf != map.IsUdmf) throw new InvalidOperationException("Map " + map.MapName + " changed format after it was added.");
				}

				byte[] originalhash = HashFile(filepath);
				string parent = Path.GetDirectoryName(filepath);
				stagingdirectory = Path.Combine(parent, ".uzb-batch-nodes-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(stagingdirectory);
				try { File.SetAttributes(stagingdirectory, File.GetAttributes(stagingdirectory) | FileAttributes.Hidden); }
				catch(Exception) { }

				string currentstage = Path.Combine(stagingdirectory, "stage-000.wad");
				File.Copy(filepath, currentstage, true);

				for(int i = 0; i < maps.Count; i++)
				{
					if(IsCancellationPending(worker))
					{
						canceled = true;
						return false;
					}

					BatchNodebuildMap map = maps[i];
					BatchNodebuildTool tool = (map.IsUdmf ? request.UdmfTool : request.BinaryTool);
					if(tool == null || !File.Exists(tool.Program))
						throw new FileNotFoundException("The configured safe ZDBSP executable was not found.", tool == null ? string.Empty : tool.Program);

					int percent = (int)Math.Round((completedbefore + i) * 100.0 / Math.Max(1, totalmaps));
					ReportProgress(worker, percent, new BatchNodebuildProgress
					{
						MapKey = map.Key,
						MapStatus = "Building...",
						StatusText = "Building " + map.MapName + " in " + Path.GetFileName(filepath) + "..."
					});

					string nextstage = Path.Combine(stagingdirectory, "stage-" + (i + 1).ToString("000") + ".wad");
					string compileroutput;
					if(!RunNodebuilder(tool, map.MapName, currentstage, nextstage, out compileroutput))
						throw new InvalidOperationException("ZDBSP failed for " + map.MapName + (string.IsNullOrEmpty(compileroutput) ? "." : ": " + compileroutput));
					if(!File.Exists(nextstage) || new FileInfo(nextstage).Length < 12)
						throw new InvalidDataException("ZDBSP did not produce a valid output WAD for " + map.MapName + ".");

					currentstage = nextstage;
				}

				ReportProgress(worker, (int)Math.Round((completedbefore + maps.Count) * 100.0 / Math.Max(1, totalmaps)),
					new BatchNodebuildProgress { StatusText = "Verifying that only selected node data changed..." });

				string verificationerror;
				HashSet<string> selectedmaps = new HashSet<string>(maps.Select(m => m.MapName), StringComparer.OrdinalIgnoreCase);
				if(!OnlySelectedNodeDataChanged(filepath, currentstage, selectedmaps, out verificationerror))
					throw new InvalidDataException("Safety check rejected the output: " + verificationerror);

				byte[] sourcehashnow = HashFile(filepath);
				if(!HashesEqual(originalhash, sourcehashnow))
					throw new IOException("The source WAD changed while nodes were being built. Run the batch again.");

				byte[] finalhash = HashFile(currentstage);
				if(HashesEqual(originalhash, finalhash)) return true;

				if(request.KeepBackups) backuppath = CreateUniqueBackupPath(filepath);
				File.Replace(currentstage, filepath, (request.KeepBackups ? backuppath : null), true);
				changed = true;
				return true;
			}
			catch(Exception ex)
			{
				error = ex.Message;
				General.WriteLogLine("Batch node rebuild rejected " + filepath + ": " + ex.GetType().Name + ": " + ex.Message);
				return false;
			}
			finally
			{
				if(!string.IsNullOrEmpty(stagingdirectory) && Directory.Exists(stagingdirectory))
				{
					try { Directory.Delete(stagingdirectory, true); }
					catch(Exception ex) { General.WriteLogLine("Unable to remove batch node staging directory: " + ex.Message); }
				}
			}
		}

		private static bool RunNodebuilder(BatchNodebuildTool tool, string mapname,
			string inputfile, string outputfile, out string output)
		{
			if(!IsSafeMapName(mapname))
				throw new InvalidDataException("Map header " + mapname + " contains characters that are unsafe for the node builder command line.");

			string arguments = "-m" + mapname + " " + tool.Parameters;
			arguments = arguments.Replace("%FI", QuoteArgument(inputfile));
			arguments = arguments.Replace("%FO", QuoteArgument(outputfile));

			ProcessStartInfo startinfo = new ProcessStartInfo();
			startinfo.FileName = tool.Program;
			startinfo.Arguments = arguments;
			startinfo.WorkingDirectory = Path.GetDirectoryName(inputfile);
			startinfo.CreateNoWindow = true;
			startinfo.ErrorDialog = false;
			startinfo.UseShellExecute = false;
			startinfo.WindowStyle = ProcessWindowStyle.Hidden;
			startinfo.RedirectStandardOutput = true;
			startinfo.RedirectStandardError = true;

			StringBuilder messages = new StringBuilder();
			using(Process process = new Process())
			{
				process.StartInfo = startinfo;
				process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
				{
					if(!string.IsNullOrEmpty(e.Data)) lock(messages) messages.AppendLine(e.Data);
				};
				process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
				{
					if(!string.IsNullOrEmpty(e.Data)) lock(messages) messages.AppendLine(e.Data);
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				process.WaitForExit();
				process.WaitForExit();
				lock(messages) output = messages.ToString().Trim();
				return process.ExitCode == 0;
			}
		}

		private static bool OnlySelectedNodeDataChanged(string original, string rebuilt,
			HashSet<string> selectedmaps, out string error)
		{
			List<LumpFingerprint> before = CreateProtectedFingerprints(original, selectedmaps);
			List<LumpFingerprint> after = CreateProtectedFingerprints(rebuilt, selectedmaps);
			if(before.Count != after.Count)
			{
				error = "the protected lump count changed (" + before.Count + " to " + after.Count + ").";
				return false;
			}

			for(int i = 0; i < before.Count; i++)
			{
				if(!before[i].Matches(after[i]))
				{
					error = "non-node lump " + before[i].Name + " at protected position " + i + " changed.";
					return false;
				}
			}

			error = string.Empty;
			return true;
		}

		private static List<LumpFingerprint> CreateProtectedFingerprints(string filepath, HashSet<string> selectedmaps)
		{
			using(WAD wad = new WAD(filepath, true))
			{
				List<MapRange> ranges = DiscoverMapRanges(wad);
				foreach(string selected in selectedmaps)
					if(!ranges.Any(r => string.Equals(r.Name, selected, StringComparison.OrdinalIgnoreCase)))
						throw new InvalidDataException("Selected map " + selected + " was missing during verification.");

				List<LumpFingerprint> result = new List<LumpFingerprint>(wad.Lumps.Count);
				using(SHA256 sha = SHA256.Create())
				{
					for(int i = 0; i < wad.Lumps.Count; i++)
					{
						Lump lump = wad.Lumps[i];
						MapRange range = ranges.FirstOrDefault(r => i >= r.StartIndex && i <= r.EndIndex
							&& selectedmaps.Contains(r.Name));
						if(range != null && (NODE_LUMPS.Contains(lump.Name)
							|| (!range.IsUdmf && (lump.Name == "VERTEXES" || lump.Name == "LINEDEFS")))) continue;

						lump.Stream.Position = 0;
						byte[] hash = sha.ComputeHash(lump.Stream);
						lump.Stream.Position = 0;
						result.Add(new LumpFingerprint(lump.Name, lump.Length, hash));
					}
				}

				return result;
			}
		}

		internal static List<MapRange> DiscoverMapRanges(WAD wad)
		{
			List<MapRange> result = new List<MapRange>();
			for(int i = 0; i < wad.Lumps.Count - 1; i++)
			{
				string header = wad.Lumps[i].Name;
				if(!IsSafeMapName(header) || BINARY_MAP_LUMPS.Contains(header) || header == "TEXTMAP" || header == "ENDMAP") continue;

				if(wad.Lumps[i + 1].Name == "TEXTMAP")
				{
					int end = -1;
					for(int p = i + 2; p < wad.Lumps.Count; p++)
					{
						if(wad.Lumps[p].Name == "ENDMAP") { end = p; break; }
						if(wad.Lumps[p].Name == "TEXTMAP") break;
					}
					if(end > -1)
					{
						result.Add(new MapRange(header, i, end, true));
						i = end;
					}
					continue;
				}

				int last = i;
				bool things = false;
				bool linedefs = false;
				bool sidedefs = false;
				bool vertices = false;
				bool sectors = false;
				for(int p = i + 1; p < wad.Lumps.Count && BINARY_MAP_LUMPS.Contains(wad.Lumps[p].Name); p++)
				{
					last = p;
					switch(wad.Lumps[p].Name)
					{
						case "THINGS": things = true; break;
						case "LINEDEFS": linedefs = true; break;
						case "SIDEDEFS": sidedefs = true; break;
						case "VERTEXES": vertices = true; break;
						case "SECTORS": sectors = true; break;
					}
				}

				if(things && linedefs && sidedefs && vertices && sectors)
				{
					result.Add(new MapRange(header, i, last, false));
					i = last;
				}
			}

			return result;
		}

		private static bool IsSafeMapName(string name)
		{
			if(string.IsNullOrEmpty(name) || name.Length > 8) return false;
			for(int i = 0; i < name.Length; i++)
			{
				char c = name[i];
				if(!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
			}
			return true;
		}

		private static string QuoteArgument(string value)
		{
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

		private static byte[] HashFile(string filepath)
		{
			using(SHA256 sha = SHA256.Create())
			using(FileStream stream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read))
				return sha.ComputeHash(stream);
		}

		private static bool HashesEqual(byte[] first, byte[] second)
		{
			if(first == null || second == null || first.Length != second.Length) return false;
			int difference = 0;
			for(int i = 0; i < first.Length; i++) difference |= first[i] ^ second[i];
			return difference == 0;
		}

		private static string CreateUniqueBackupPath(string filepath)
		{
			string basepath = filepath + ".before-batch-nodes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string candidate = basepath + ".bak";
			int index = 2;
			while(File.Exists(candidate)) candidate = basepath + "-" + (index++).ToString() + ".bak";
			return candidate;
		}

		private static void ReportMaps(BackgroundWorker worker, int completed, int total,
			IEnumerable<BatchNodebuildMap> maps, string status, string logtext)
		{
			int percent = (int)Math.Round(completed * 100.0 / Math.Max(1, total));
			bool first = true;
			foreach(BatchNodebuildMap map in maps)
			{
				ReportProgress(worker, percent, new BatchNodebuildProgress
				{
					MapKey = map.Key,
					MapStatus = status,
					StatusText = "Processed " + completed + " of " + total + " maps",
					LogText = (first ? logtext : string.Empty)
				});
				first = false;
			}
		}

		private static bool IsCancellationPending(BackgroundWorker worker)
		{
			return worker != null && worker.CancellationPending;
		}

		private static void ReportProgress(BackgroundWorker worker, int percent, BatchNodebuildProgress progress)
		{
			if(worker != null) worker.ReportProgress(percent, progress);
		}

		internal sealed class MapRange
		{
			internal readonly string Name;
			internal readonly int StartIndex;
			internal readonly int EndIndex;
			internal readonly bool IsUdmf;

			internal MapRange(string name, int startindex, int endindex, bool isudmf)
			{
				Name = name;
				StartIndex = startindex;
				EndIndex = endindex;
				IsUdmf = isudmf;
			}
		}

		private sealed class LumpFingerprint
		{
			internal readonly string Name;
			private readonly int length;
			private readonly byte[] hash;

			internal LumpFingerprint(string name, int length, byte[] hash)
			{
				Name = name;
				this.length = length;
				this.hash = hash;
			}

			internal bool Matches(LumpFingerprint other)
			{
				return other != null && string.Equals(Name, other.Name, StringComparison.Ordinal)
					&& length == other.length && HashesEqual(hash, other.hash);
			}
		}
	}

	#endregion
}
