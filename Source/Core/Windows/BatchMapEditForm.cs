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
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Properties;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	/// <summary>
	/// Safely previews and applies the same texture or Thing edit to selected maps
	/// in one or more WADs. Source files are never edited in place: each WAD is
	/// changed in a staging copy, verified, and then replaced atomically.
	/// </summary>
	internal sealed class BatchMapEditForm : DelayedForm
	{
		private readonly ListView mapslist;
		private readonly Button addfiles;
		private readonly Button removefiles;
		private readonly Button selectall;
		private readonly Button selectnone;
		private readonly Button preview;
		private readonly Button apply;
		private readonly Button cancel;
		private readonly Button close;
		private readonly CheckBox keepbackups;
		private readonly Label selectionsummary;
		private readonly Label progresslabel;
		private readonly ProgressBar progressbar;
		private readonly RichTextBox log;
		private readonly BackgroundWorker worker;
		private readonly Dictionary<string, ListViewItem> mapitems;

		private readonly TabControl operationtabs;
		private readonly TextBox findtexture;
		private readonly TextBox replacetexture;
		private readonly CheckBox texturetop;
		private readonly CheckBox texturemiddle;
		private readonly CheckBox texturebottom;
		private readonly CheckBox texturefloor;
		private readonly CheckBox textureceiling;

		private readonly TextBox thingtypes;
		private readonly Button browsethingtypes;
		private readonly CheckBox setthingtype;
		private readonly TextBox newthingtype;
		private readonly Button browsenewthingtype;
		private readonly CheckBox setthingtag;
		private readonly TextBox newthingtag;
		private readonly CheckBox setthingangle;
		private readonly TextBox newthingangle;
		private readonly TextBox enableudmfflags;
		private readonly TextBox disableudmfflags;
		private readonly TextBox enablebinaryflags;
		private readonly TextBox disablebinaryflags;
		private readonly Button browseflags;

		private string previewsignature;
		private int previewchanges;
		private Dictionary<string, string> previewfilehashes;

		internal BatchMapEditForm()
		{
			Text = "Batch Edit Maps";
			Icon = Resources.UZB;
			ClientSize = new Size(980, 760);
			MinimumSize = new Size(780, 650);
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = true;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			Opacity = 0.0;
			AllowDrop = true;

			mapitems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
			previewfilehashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			TableLayoutPanel layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.Padding = new Padding(10);
			layout.ColumnCount = 1;
			layout.RowCount = 8;
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

			Label explanation = new Label();
			explanation.Dock = DockStyle.Fill;
			explanation.BackColor = SystemColors.Info;
			explanation.ForeColor = SystemColors.InfoText;
			explanation.BorderStyle = BorderStyle.FixedSingle;
			explanation.Padding = new Padding(8, 6, 8, 6);
			explanation.Text = "Choose WADs and maps, configure one edit, then Preview. Apply is enabled only for that exact preview. "
				+ "The tool supports UDMF and binary maps, protects unrelated lumps, and never overwrites a WAD that is currently open in UZB.";
			layout.Controls.Add(explanation, 0, 0);

			FlowLayoutPanel toolbar = new FlowLayoutPanel();
			toolbar.Dock = DockStyle.Fill;
			toolbar.FlowDirection = FlowDirection.LeftToRight;
			toolbar.WrapContents = false;
			toolbar.Padding = new Padding(0, 5, 0, 0);

			addfiles = MakeButton("Add WAD files...", addfiles_Click);
			removefiles = MakeButton("Remove selected", removefiles_Click);
			selectall = MakeButton("Check all", selectall_Click);
			selectnone = MakeButton("Check none", selectnone_Click);
			removefiles.Enabled = selectall.Enabled = selectnone.Enabled = false;
			toolbar.Controls.Add(addfiles);
			toolbar.Controls.Add(removefiles);
			toolbar.Controls.Add(selectall);
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
			mapslist.Columns.Add("Folder", 360);
			mapslist.Columns.Add("Preview / status", 210);
			mapslist.ItemCheck += mapslist_ItemCheck;
			mapslist.SelectedIndexChanged += mapslist_SelectedIndexChanged;
			mapslist.KeyDown += mapslist_KeyDown;
			layout.Controls.Add(mapslist, 0, 2);

			operationtabs = new TabControl();
			operationtabs.Dock = DockStyle.Fill;
			operationtabs.SelectedIndexChanged += EditSettingsChanged;
			TabPage texturetab = new TabPage("Replace textures");
			TabPage thingtab = new TabPage("Edit Things");
			operationtabs.TabPages.Add(texturetab);
			operationtabs.TabPages.Add(thingtab);
			layout.Controls.Add(operationtabs, 0, 3);

			TableLayoutPanel texturelayout = MakeSettingsLayout(6);
			findtexture = AddLabeledTextBox(texturelayout, 0, "Find exact texture/flat name:");
			replacetexture = AddLabeledTextBox(texturelayout, 1, "Replace with:");
			FlowLayoutPanel scopes = new FlowLayoutPanel();
			scopes.Dock = DockStyle.Fill;
			scopes.WrapContents = true;
			scopes.Padding = new Padding(0, 3, 0, 0);
			texturetop = MakeCheckBox("Upper walls", true);
			texturemiddle = MakeCheckBox("Middle walls", true);
			texturebottom = MakeCheckBox("Lower walls", true);
			texturefloor = MakeCheckBox("Floors", true);
			textureceiling = MakeCheckBox("Ceilings", true);
			scopes.Controls.Add(texturetop);
			scopes.Controls.Add(texturemiddle);
			scopes.Controls.Add(texturebottom);
			scopes.Controls.Add(texturefloor);
			scopes.Controls.Add(textureceiling);
			texturelayout.Controls.Add(MakeLabel("Replace in:"), 0, 2);
			texturelayout.Controls.Add(scopes, 1, 2);
			Label texturehelp = MakeHelpLabel("Names are matched case-insensitively. Binary-map names must fit Doom's 8-character limit; UDMF names are not truncated.");
			texturelayout.Controls.Add(texturehelp, 0, 3);
			texturelayout.SetColumnSpan(texturehelp, 2);
			texturetab.Controls.Add(texturelayout);

			TableLayoutPanel thinglayout = MakeSettingsLayout(7);
			Panel thingtypepanel = new Panel();
			thingtypepanel.Dock = DockStyle.Fill;
			thingtypes = new TextBox();
			thingtypes.Dock = DockStyle.Fill;
			browsethingtypes = new Button();
			browsethingtypes.Text = "Browse...";
			browsethingtypes.AutoSize = true;
			browsethingtypes.Dock = DockStyle.Right;
			browsethingtypes.Click += browsethingtypes_Click;
			thingtypepanel.Controls.Add(thingtypes);
			thingtypepanel.Controls.Add(browsethingtypes);
			thinglayout.Controls.Add(MakeLabel("Match Thing type(s):"), 0, 0);
			thinglayout.Controls.Add(thingtypepanel, 1, 0);

			FlowLayoutPanel corechanges = new FlowLayoutPanel();
			corechanges.Dock = DockStyle.Fill;
			corechanges.WrapContents = false;
			setthingtype = MakeCheckBox("Set type", false);
			newthingtype = MakeSmallTextBox("0");
			browsenewthingtype = MakeButton("Browse type...", browsenewthingtype_Click);
			browsenewthingtype.Margin = new Padding(0, 0, 14, 0);
			setthingtag = MakeCheckBox("Set tag", false);
			newthingtag = MakeSmallTextBox("0");
			setthingangle = MakeCheckBox("Set angle", false);
			newthingangle = MakeSmallTextBox("0");
			corechanges.Controls.Add(setthingtype);
			corechanges.Controls.Add(newthingtype);
			corechanges.Controls.Add(browsenewthingtype);
			corechanges.Controls.Add(setthingtag);
			corechanges.Controls.Add(newthingtag);
			corechanges.Controls.Add(setthingangle);
			corechanges.Controls.Add(newthingangle);
			browseflags = new Button();
			browseflags.AutoSize = true;
			browseflags.Text = "Choose flags...";
			browseflags.Margin = new Padding(0, 0, 0, 0);
			browseflags.Click += browseflags_Click;
			corechanges.Controls.Add(browseflags);
			thinglayout.Controls.Add(MakeLabel("Properties:"), 0, 1);
			thinglayout.Controls.Add(corechanges, 1, 1);

			enableudmfflags = AddLabeledTextBox(thinglayout, 2, "UDMF flags to turn ON:");
			disableudmfflags = AddLabeledTextBox(thinglayout, 3, "UDMF flags to turn OFF:");
			enablebinaryflags = AddLabeledTextBox(thinglayout, 4, "Binary flag bits to turn ON:");
			disablebinaryflags = AddLabeledTextBox(thinglayout, 5, "Binary flag bits to turn OFF:");
			enablebinaryflags.Text = "0";
			disablebinaryflags.Text = "0";
			Label thinghelp = MakeHelpLabel("Enter Thing types separated by commas, just like Find and Replace. Choose flags uses the currently open map's named flag list. "
				+ "You may also enter UDMF flag names directly; binary flags accept a decimal or 0x hexadecimal bit mask. Doom-format Things have no tag field; Hexen and UDMF Things do.");
			thinglayout.Controls.Add(thinghelp, 0, 6);
			thinglayout.SetColumnSpan(thinghelp, 2);
			thingtab.Controls.Add(thinglayout);

			Panel safety = new Panel();
			safety.Dock = DockStyle.Fill;
			keepbackups = new CheckBox();
			keepbackups.AutoSize = true;
			keepbackups.Checked = true;
			keepbackups.Location = new Point(0, 8);
			keepbackups.Text = "Keep a timestamped backup beside each changed WAD (recommended)";
			safety.Controls.Add(keepbackups);
			layout.Controls.Add(safety, 0, 4);

			TableLayoutPanel progresslayout = new TableLayoutPanel();
			progresslayout.Dock = DockStyle.Fill;
			progresslayout.ColumnCount = 2;
			progresslayout.RowCount = 1;
			progresslayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
			progresslayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			progresslabel = new Label();
			progresslabel.Dock = DockStyle.Fill;
			progresslabel.TextAlign = ContentAlignment.MiddleLeft;
			progresslabel.Text = "Ready — preview is required before applying";
			progressbar = new ProgressBar();
			progressbar.Dock = DockStyle.Fill;
			progressbar.Style = ProgressBarStyle.Continuous;
			progresslayout.Controls.Add(progresslabel, 0, 0);
			progresslayout.Controls.Add(progressbar, 1, 0);
			layout.Controls.Add(progresslayout, 0, 5);

			log = new RichTextBox();
			log.Dock = DockStyle.Fill;
			log.ReadOnly = true;
			log.BackColor = SystemColors.Window;
			log.DetectUrls = false;
			log.WordWrap = true;
			log.Text = "Add WAD files or drag them onto this window. Configure the edit, then Preview to see exact per-map match counts before anything is written.";
			layout.Controls.Add(log, 0, 6);

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
			cancel = MakeButton("Cancel", cancel_Click);
			cancel.Enabled = false;
			buttons.Controls.Add(cancel);
			apply = MakeButton("Apply previewed edit", apply_Click);
			apply.Enabled = false;
			buttons.Controls.Add(apply);
			preview = MakeButton("Preview selected maps", preview_Click);
			preview.Enabled = false;
			buttons.Controls.Add(preview);
			layout.Controls.Add(buttons, 0, 7);

			Controls.Add(layout);
			CancelButton = close;
			AcceptButton = preview;

			HookSettingsChanges(this);
			worker = new BackgroundWorker();
			worker.WorkerReportsProgress = true;
			worker.WorkerSupportsCancellation = true;
			worker.DoWork += worker_DoWork;
			worker.ProgressChanged += worker_ProgressChanged;
			worker.RunWorkerCompleted += worker_RunWorkerCompleted;

			DragEnter += BatchMapEditForm_DragEnter;
			DragDrop += BatchMapEditForm_DragDrop;
		}

		private static Button MakeButton(string text, EventHandler click)
		{
			Button result = new Button();
			result.AutoSize = true;
			result.Text = text;
			result.Click += click;
			return result;
		}

		private static CheckBox MakeCheckBox(string text, bool value)
		{
			CheckBox result = new CheckBox();
			result.AutoSize = true;
			result.Text = text;
			result.Checked = value;
			result.Margin = new Padding(3, 3, 12, 3);
			return result;
		}

		private static TextBox MakeSmallTextBox(string text)
		{
			TextBox result = new TextBox();
			result.Text = text;
			result.Width = 58;
			result.Margin = new Padding(0, 1, 14, 0);
			return result;
		}

		private static Label MakeLabel(string text)
		{
			Label result = new Label();
			result.Text = text;
			result.Dock = DockStyle.Fill;
			result.TextAlign = ContentAlignment.MiddleRight;
			result.AutoEllipsis = true;
			return result;
		}

		private static Label MakeHelpLabel(string text)
		{
			Label result = new Label();
			result.Text = text;
			result.Dock = DockStyle.Fill;
			result.Padding = new Padding(4);
			result.BackColor = SystemColors.ControlLight;
			result.BorderStyle = BorderStyle.FixedSingle;
			return result;
		}

		private static TableLayoutPanel MakeSettingsLayout(int rows)
		{
			TableLayoutPanel result = new TableLayoutPanel();
			result.Dock = DockStyle.Fill;
			result.Padding = new Padding(10);
			result.ColumnCount = 2;
			result.RowCount = rows;
			result.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
			result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			for(int i = 0; i < rows - 1; i++) result.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
			result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			return result;
		}

		private static TextBox AddLabeledTextBox(TableLayoutPanel layout, int row, string label)
		{
			TextBox result = new TextBox();
			result.Dock = DockStyle.Fill;
			layout.Controls.Add(MakeLabel(label), 0, row);
			layout.Controls.Add(result, 1, row);
			return result;
		}

		private void HookSettingsChanges(Control parent)
		{
			foreach(Control control in parent.Controls)
			{
				if(control == mapslist || control == log) continue;
				TextBox textbox = control as TextBox;
				CheckBox checkbox = control as CheckBox;
				if(textbox != null) textbox.TextChanged += EditSettingsChanged;
				if(checkbox != null && checkbox != keepbackups) checkbox.CheckedChanged += EditSettingsChanged;
				if(control.HasChildren) HookSettingsChanges(control);
			}
		}

		private void addfiles_Click(object sender, EventArgs e)
		{
			using(OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = "Add WAD files for batch editing";
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
						AppendLog("Skipped " + Path.GetFileName(fullpath) + ": it is currently open in UZB. Save and close it before batch editing.");
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

			InvalidatePreview();
			UpdateSelectionSummary();
		}

		private void removefiles_Click(object sender, EventArgs e)
		{
			foreach(ListViewItem item in mapslist.SelectedItems.Cast<ListViewItem>().ToList())
			{
				BatchNodebuildMap map = item.Tag as BatchNodebuildMap;
				if(map != null) mapitems.Remove(map.Key);
				mapslist.Items.Remove(item);
			}

			foreach(ListViewGroup group in mapslist.Groups.Cast<ListViewGroup>().ToList())
				if(group.Items.Count == 0) mapslist.Groups.Remove(group);
			InvalidatePreview();
			UpdateSelectionSummary();
		}

		private void selectall_Click(object sender, EventArgs e)
		{
			foreach(ListViewItem item in mapslist.Items) item.Checked = true;
			BeginInvoke((MethodInvoker)delegate { InvalidatePreview(); UpdateSelectionSummary(); });
		}

		private void selectnone_Click(object sender, EventArgs e)
		{
			foreach(ListViewItem item in mapslist.Items) item.Checked = false;
			BeginInvoke((MethodInvoker)delegate { InvalidatePreview(); UpdateSelectionSummary(); });
		}

		private void mapslist_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			BeginInvoke((MethodInvoker)delegate { InvalidatePreview(); UpdateSelectionSummary(); });
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

		private void browsethingtypes_Click(object sender, EventArgs e)
		{
			if(General.Map == null)
			{
				MessageBox.Show(this, "Open any map that uses the desired game configuration before using the Thing browser. You can also enter type numbers directly.",
					"Thing Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			int[] types;
			if(!TryParseTypes(thingtypes.Text, out types)) types = new int[0];
			types = ThingMultipleBrowserForm.BrowseThings(this, types);
			thingtypes.Text = string.Join(",", types.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToArray());
		}

		private void browsenewthingtype_Click(object sender, EventArgs e)
		{
			if(General.Map == null)
			{
				MessageBox.Show(this, "Open any map that uses the desired game configuration before using the Thing browser. You can also enter a type number directly.",
					"Thing Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			int initialtype;
			if(!int.TryParse(newthingtype.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out initialtype)) initialtype = 0;
			using(ThingBrowserForm browser = new ThingBrowserForm(initialtype))
			{
				if(browser.ShowDialog(this) != DialogResult.OK) return;
				newthingtype.Text = browser.SelectedType.ToString(CultureInfo.InvariantCulture);
				setthingtype.Checked = true;
			}
		}

		private void browseflags_Click(object sender, EventArgs e)
		{
			if(General.Map == null)
			{
				MessageBox.Show(this, "Open any map that uses the desired game configuration before choosing named flags. You can also enter flag names or binary bits directly.",
					"Thing Flags", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			string initial;
			if(General.Map.UDMF)
			{
				List<string> values = new List<string>();
				values.AddRange(SplitFlagNames(enableudmfflags.Text));
				values.AddRange(SplitFlagNames(disableudmfflags.Text).Select(f => "!" + f));
				initial = string.Join(",", values.ToArray());
			}
			else
			{
				List<string> values = new List<string>();
				foreach(string key in General.Map.Config.ThingFlags.Keys)
				{
					int bit;
					if(!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out bit)) continue;
					if((ParseMaskOrZero(enablebinaryflags.Text) & bit) == bit) values.Add(key);
					else if((ParseMaskOrZero(disablebinaryflags.Text) & bit) == bit) values.Add("!" + key);
				}
				initial = string.Join(",", values.ToArray());
			}

			string selected = FlagsForm.ShowDialog(this, initial, General.Map.Config.ThingFlags);
			if(General.Map.UDMF)
			{
				enableudmfflags.Text = string.Join(",", SplitFlagNames(selected).Where(f => !f.StartsWith("!", StringComparison.Ordinal)).ToArray());
				disableudmfflags.Text = string.Join(",", SplitFlagNames(selected).Where(f => f.StartsWith("!", StringComparison.Ordinal)).Select(f => f.Substring(1)).ToArray());
			}
			else
			{
				int enabled = 0;
				int disabled = 0;
				foreach(string value in SplitFlagNames(selected))
				{
					bool off = value.StartsWith("!", StringComparison.Ordinal);
					string key = off ? value.Substring(1) : value;
					int bit;
					if(int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out bit))
					{
						if(off) disabled |= bit; else enabled |= bit;
					}
				}
				enablebinaryflags.Text = enabled.ToString(CultureInfo.InvariantCulture);
				disablebinaryflags.Text = disabled.ToString(CultureInfo.InvariantCulture);
			}
		}

		private static IEnumerable<string> SplitFlagNames(string value)
		{
			return (value ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim());
		}

		private static int ParseMaskOrZero(string value)
		{
			int result;
			return TryParseMask(value, out result) ? result : 0;
		}

		private void EditSettingsChanged(object sender, EventArgs e)
		{
			if(worker != null && worker.IsBusy) return;
			InvalidatePreview();
		}

		private void InvalidatePreview()
		{
			previewsignature = null;
			previewchanges = 0;
			previewfilehashes.Clear();
			if(apply != null) apply.Enabled = false;
			if(progresslabel != null && (worker == null || !worker.IsBusy)) progresslabel.Text = "Ready — preview is required before applying";
			if(mapslist != null)
				foreach(ListViewItem item in mapslist.Items) item.SubItems[4].Text = "Ready";
		}

		private void UpdateSelectionSummary()
		{
			int selectedmaps = mapslist.CheckedItems.Count;
			int selectedfiles = mapslist.CheckedItems.Cast<ListViewItem>()
				.Select(i => ((BatchNodebuildMap)i.Tag).FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			selectionsummary.Text = (mapslist.Items.Count == 0 ? "No maps added"
				: selectedmaps + " of " + mapslist.Items.Count + " maps checked in " + selectedfiles + " WAD" + (selectedfiles == 1 ? "" : "s"));
			preview.Enabled = !worker.IsBusy && selectedmaps > 0;
			selectall.Enabled = !worker.IsBusy && mapslist.Items.Count > 0;
			selectnone.Enabled = !worker.IsBusy && mapslist.Items.Count > 0;
			removefiles.Enabled = !worker.IsBusy && mapslist.SelectedItems.Count > 0;
			apply.Enabled = !worker.IsBusy && previewchanges > 0 && previewsignature == ComputeSignature();
		}

		private void preview_Click(object sender, EventArgs e)
		{
			BatchMapEditOperation operation;
			string error;
			if(!TryBuildOperation(out operation, out error))
			{
				MessageBox.Show(this, error, "Batch Edit Maps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			List<BatchNodebuildMap> maps = GetCheckedMaps();
			if(maps.Count == 0) return;
			foreach(ListViewItem item in mapslist.CheckedItems) item.SubItems[4].Text = "Waiting";
			previewsignature = null;
			previewchanges = 0;
			previewfilehashes.Clear();
			SetBusy(true);
			progressbar.Value = 0;
			progresslabel.Text = "Previewing selected maps...";
			AppendLog("Previewing " + maps.Count + " selected map" + (maps.Count == 1 ? "" : "s") + " without writing any files.");
			worker.RunWorkerAsync(new BatchMapEditRequest(maps, operation, true, keepbackups.Checked, ComputeSignature(), null));
		}

		private void apply_Click(object sender, EventArgs e)
		{
			BatchMapEditOperation operation;
			string error;
			if(!TryBuildOperation(out operation, out error))
			{
				MessageBox.Show(this, error, "Batch Edit Maps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string signature = ComputeSignature();
			if(previewsignature != signature || previewchanges < 1)
			{
				MessageBox.Show(this, "The maps or edit settings changed. Run Preview again before applying.", "Batch Edit Maps",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				InvalidatePreview();
				return;
			}

			List<BatchNodebuildMap> maps = GetCheckedMaps();
			DialogResult confirm = MessageBox.Show(this,
				"Apply the previewed edit to " + maps.Count + " selected map" + (maps.Count == 1 ? "" : "s") + "?"
				+ Environment.NewLine + Environment.NewLine + (keepbackups.Checked ? "Timestamped backups will be kept." : "Backups are disabled."),
				"Apply Batch Edit", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if(confirm != DialogResult.OK) return;

			foreach(ListViewItem item in mapslist.CheckedItems) item.SubItems[4].Text = "Waiting";
			SetBusy(true);
			progressbar.Value = 0;
			progresslabel.Text = "Applying verified batch edit...";
			AppendLog("Applying the exact previewed edit. Each WAD will be staged and verified before replacement.");
			worker.RunWorkerAsync(new BatchMapEditRequest(maps, operation, false, keepbackups.Checked, signature,
				new Dictionary<string, string>(previewfilehashes, StringComparer.OrdinalIgnoreCase)));
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			if(!worker.IsBusy) return;
			worker.CancelAsync();
			cancel.Enabled = false;
			progresslabel.Text = "Cancel requested; finishing the current file safely...";
			AppendLog("Cancel requested. No partially edited WAD will be installed.");
		}

		private void SetBusy(bool busy)
		{
			addfiles.Enabled = !busy;
			mapslist.Enabled = !busy;
			operationtabs.Enabled = !busy;
			keepbackups.Enabled = !busy;
			close.Enabled = !busy;
			cancel.Enabled = busy;
			selectall.Enabled = !busy && mapslist.Items.Count > 0;
			selectnone.Enabled = !busy && mapslist.Items.Count > 0;
			removefiles.Enabled = !busy && mapslist.SelectedItems.Count > 0;
			preview.Enabled = !busy && mapslist.CheckedItems.Count > 0;
			apply.Enabled = !busy && previewchanges > 0 && previewsignature == ComputeSignature();
		}

		private List<BatchNodebuildMap> GetCheckedMaps()
		{
			return mapslist.CheckedItems.Cast<ListViewItem>().Select(i => (BatchNodebuildMap)i.Tag).ToList();
		}

		private bool TryBuildOperation(out BatchMapEditOperation operation, out string error)
		{
			operation = new BatchMapEditOperation();
			error = string.Empty;
			if(operationtabs.SelectedIndex == 0)
			{
				operation.Kind = BatchMapEditKind.Textures;
				operation.FindTexture = findtexture.Text.Trim();
				operation.ReplaceTexture = replacetexture.Text.Trim();
				operation.TextureTop = texturetop.Checked;
				operation.TextureMiddle = texturemiddle.Checked;
				operation.TextureBottom = texturebottom.Checked;
				operation.TextureFloor = texturefloor.Checked;
				operation.TextureCeiling = textureceiling.Checked;
				if(string.IsNullOrEmpty(operation.FindTexture) || string.IsNullOrEmpty(operation.ReplaceTexture))
				{
					error = "Enter both the exact texture to find and its replacement.";
					return false;
				}
				if(string.Equals(operation.FindTexture, operation.ReplaceTexture, StringComparison.OrdinalIgnoreCase))
				{
					error = "The find and replacement texture names are the same.";
					return false;
				}
				if(!(operation.TextureTop || operation.TextureMiddle || operation.TextureBottom || operation.TextureFloor || operation.TextureCeiling))
				{
					error = "Choose at least one wall or flat location to replace.";
					return false;
				}
				return true;
			}

			operation.Kind = BatchMapEditKind.Things;
			int[] types;
			if(!TryParseTypes(thingtypes.Text, out types) || types.Length == 0)
			{
				error = "Enter one or more Thing type numbers separated by commas.";
				return false;
			}
			operation.ThingTypes = new HashSet<int>(types);
			if(types.Any(t => t < 0 || t > 65535))
			{
				error = "Thing type numbers must be between 0 and 65535.";
				return false;
			}

			operation.SetThingType = setthingtype.Checked;
			operation.SetThingTag = setthingtag.Checked;
			operation.SetThingAngle = setthingangle.Checked;
			if(operation.SetThingType && !TryParseRange(newthingtype.Text, 0, 65535, out operation.NewThingType))
			{
				error = "The replacement Thing type must be between 0 and 65535.";
				return false;
			}
			if(operation.SetThingTag && !TryParseRange(newthingtag.Text, 0, 65535, out operation.NewThingTag))
			{
				error = "The Thing tag must be between 0 and 65535.";
				return false;
			}
			if(operation.SetThingAngle && !TryParseRange(newthingangle.Text, 0, 359, out operation.NewThingAngle))
			{
				error = "The Thing angle must be between 0 and 359 degrees.";
				return false;
			}

			if(!TryParseFlagNames(enableudmfflags.Text, out operation.EnableUdmfFlags, out error)
				|| !TryParseFlagNames(disableudmfflags.Text, out operation.DisableUdmfFlags, out error)) return false;
			if(operation.EnableUdmfFlags.Overlaps(operation.DisableUdmfFlags))
			{
				error = "A UDMF flag cannot be listed in both the ON and OFF boxes.";
				return false;
			}
			if(!TryParseMask(enablebinaryflags.Text, out operation.EnableBinaryFlags)
				|| !TryParseMask(disablebinaryflags.Text, out operation.DisableBinaryFlags))
			{
				error = "Binary flag masks must be between 0 and 65535, in decimal or 0x hexadecimal form.";
				return false;
			}
			if((operation.EnableBinaryFlags & operation.DisableBinaryFlags) != 0)
			{
				error = "The same binary flag bit cannot be turned both ON and OFF.";
				return false;
			}

			if(!(operation.SetThingType || operation.SetThingTag || operation.SetThingAngle
				|| operation.EnableUdmfFlags.Count > 0 || operation.DisableUdmfFlags.Count > 0
				|| operation.EnableBinaryFlags != 0 || operation.DisableBinaryFlags != 0))
			{
				error = "Choose at least one Thing property or flag to change.";
				return false;
			}

			return true;
		}

		private static bool TryParseTypes(string value, out int[] result)
		{
			List<int> values = new List<int>();
			foreach(string part in (value ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int parsed;
				if(!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
				{
					result = new int[0];
					return false;
				}
				if(!values.Contains(parsed)) values.Add(parsed);
			}
			result = values.ToArray();
			return true;
		}

		private static bool TryParseRange(string value, int min, int max, out int result)
		{
			return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
				&& result >= min && result <= max;
		}

		private static bool TryParseMask(string value, out int result)
		{
			string text = (value ?? string.Empty).Trim();
			if(string.IsNullOrEmpty(text)) { result = 0; return true; }
			bool parsed;
			if(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				parsed = int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
			else parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
			return parsed && result >= 0 && result <= 65535;
		}

		private static bool TryParseFlagNames(string value, out HashSet<string> result, out string error)
		{
			result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			error = string.Empty;
			foreach(string part in (value ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string flag = part.Trim();
				if(!BatchUdmfEditor.IsIdentifier(flag))
				{
					error = "UDMF flag name '" + flag + "' is not valid. Use letters, numbers, and underscores, beginning with a letter or underscore.";
					return false;
				}
				result.Add(flag);
			}
			return true;
		}

		private string ComputeSignature()
		{
			StringBuilder value = new StringBuilder();
			foreach(BatchNodebuildMap map in GetCheckedMaps().OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase)) value.Append(map.Key).Append('|');
			value.Append(operationtabs.SelectedIndex).Append('|').Append(findtexture.Text).Append('|').Append(replacetexture.Text)
				.Append('|').Append(texturetop.Checked).Append(texturemiddle.Checked).Append(texturebottom.Checked).Append(texturefloor.Checked).Append(textureceiling.Checked)
				.Append('|').Append(thingtypes.Text).Append('|').Append(setthingtype.Checked).Append('|').Append(newthingtype.Text)
				.Append('|').Append(setthingtag.Checked).Append('|').Append(newthingtag.Text).Append('|').Append(setthingangle.Checked).Append('|').Append(newthingangle.Text)
				.Append('|').Append(enableudmfflags.Text).Append('|').Append(disableudmfflags.Text)
				.Append('|').Append(enablebinaryflags.Text).Append('|').Append(disablebinaryflags.Text);
			using(SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString()))).Replace("-", string.Empty);
		}

		private void worker_DoWork(object sender, DoWorkEventArgs e)
		{
			try { e.Result = BatchMapEditEngine.Run((BatchMapEditRequest)e.Argument, worker); }
			catch(Exception ex) { e.Result = BatchMapEditResult.FromFatalError((BatchMapEditRequest)e.Argument, ex); }
		}

		private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			progressbar.Value = Math.Max(progressbar.Minimum, Math.Min(progressbar.Maximum, e.ProgressPercentage));
			BatchMapEditProgress progress = e.UserState as BatchMapEditProgress;
			if(progress == null) return;
			if(!string.IsNullOrEmpty(progress.StatusText)) progresslabel.Text = progress.StatusText;
			if(!string.IsNullOrEmpty(progress.LogText)) AppendLog(progress.LogText);
			if(!string.IsNullOrEmpty(progress.MapKey) && mapitems.ContainsKey(progress.MapKey))
				mapitems[progress.MapKey].SubItems[4].Text = progress.MapStatus;
		}

		private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			SetBusy(false);
			BatchMapEditResult result = e.Result as BatchMapEditResult;
			if(result == null)
			{
				progresslabel.Text = "Batch failed";
				AppendLog("The batch ended unexpectedly. No unverified staging file was installed.");
			}
			else
			{
				if(!result.Canceled) progressbar.Value = 100;
				progresslabel.Text = result.Summary;
				AppendLog(result.Summary);
				if(result.Request.PreviewOnly && !result.Canceled && result.FilesFailed == 0
					&& result.FatalError == null && result.Changes > 0 && result.Request.Signature == ComputeSignature())
				{
					previewsignature = result.Request.Signature;
					previewchanges = result.Changes;
					previewfilehashes = new Dictionary<string, string>(result.FileHashes, StringComparer.OrdinalIgnoreCase);
					apply.Enabled = true;
				}
				else if(!result.Request.PreviewOnly)
				{
					previewsignature = null;
					previewchanges = 0;
					previewfilehashes.Clear();
					apply.Enabled = false;
				}
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

		private void BatchMapEditForm_DragEnter(object sender, DragEventArgs e)
		{
			if(!worker.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
			else e.Effect = DragDropEffects.None;
		}

		private void BatchMapEditForm_DragDrop(object sender, DragEventArgs e)
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
				progresslabel.Text = "Cancel requested; finishing the current file safely...";
				return;
			}
			base.OnFormClosing(e);
		}
	}

	internal enum BatchMapEditKind { Textures, Things }

	internal sealed class BatchMapEditOperation
	{
		internal BatchMapEditKind Kind;
		internal string FindTexture;
		internal string ReplaceTexture;
		internal bool TextureTop;
		internal bool TextureMiddle;
		internal bool TextureBottom;
		internal bool TextureFloor;
		internal bool TextureCeiling;
		internal HashSet<int> ThingTypes = new HashSet<int>();
		internal bool SetThingType;
		internal int NewThingType;
		internal bool SetThingTag;
		internal int NewThingTag;
		internal bool SetThingAngle;
		internal int NewThingAngle;
		internal HashSet<string> EnableUdmfFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal HashSet<string> DisableUdmfFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal int EnableBinaryFlags;
		internal int DisableBinaryFlags;
	}

	internal sealed class BatchMapEditRequest
	{
		internal readonly List<BatchNodebuildMap> Maps;
		internal readonly BatchMapEditOperation Operation;
		internal readonly bool PreviewOnly;
		internal readonly bool KeepBackups;
		internal readonly string Signature;
		internal readonly Dictionary<string, string> ExpectedFileHashes;

		internal BatchMapEditRequest(List<BatchNodebuildMap> maps, BatchMapEditOperation operation,
			bool previewonly, bool keepbackups, string signature, Dictionary<string, string> expectedfilehashes)
		{
			Maps = maps;
			Operation = operation;
			PreviewOnly = previewonly;
			KeepBackups = keepbackups;
			Signature = signature;
			ExpectedFileHashes = expectedfilehashes;
		}
	}

	internal sealed class BatchMapEditProgress
	{
		internal string StatusText;
		internal string LogText;
		internal string MapKey;
		internal string MapStatus;
	}

	internal sealed class BatchMapEditOutcome
	{
		internal int Matches;
		internal int Changes;
	}

	internal sealed class BatchMapEditResult
	{
		internal BatchMapEditRequest Request;
		internal int MapsCompleted;
		internal int MapsMatched;
		internal int Matches;
		internal int Changes;
		internal int FilesUpdated;
		internal int FilesUnchanged;
		internal int FilesFailed;
		internal bool Canceled;
		internal string FatalError;
		internal readonly Dictionary<string, string> FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		internal string Summary
		{
			get
			{
				if(!string.IsNullOrEmpty(FatalError)) return "Batch failed: " + FatalError;
				if(Canceled) return "Batch canceled safely; completed WADs remain updated, and no partial WAD was installed.";
				if(Request.PreviewOnly)
					return "Preview finished: " + Matches + " match" + (Matches == 1 ? "" : "es") + " in " + MapsMatched
						+ " map" + (MapsMatched == 1 ? "" : "s") + "; " + Changes + " would change"
						+ (FilesFailed > 0 ? "; " + FilesFailed + " WAD" + (FilesFailed == 1 ? "" : "s") + " could not be scanned" : "") + ".";
				return "Batch edit finished: " + Changes + " change" + (Changes == 1 ? "" : "s") + " across " + MapsMatched
					+ " map" + (MapsMatched == 1 ? "" : "s") + "; " + FilesUpdated + " WAD" + (FilesUpdated == 1 ? "" : "s") + " updated"
					+ (FilesUnchanged > 0 ? ", " + FilesUnchanged + " unchanged" : "")
					+ (FilesFailed > 0 ? ", " + FilesFailed + " failed with originals untouched" : "") + ".";
			}
		}

		internal static BatchMapEditResult FromFatalError(BatchMapEditRequest request, Exception ex)
		{
			return new BatchMapEditResult { Request = request, FatalError = ex.GetType().Name + ": " + ex.Message, FilesFailed = 1 };
		}
	}

	internal static class BatchMapEditEngine
	{
		internal static BatchMapEditResult Run(BatchMapEditRequest request, BackgroundWorker worker)
		{
			BatchMapEditResult result = new BatchMapEditResult();
			result.Request = request;
			List<IGrouping<string, BatchNodebuildMap>> files = request.Maps.GroupBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
			int completed = 0;

			foreach(IGrouping<string, BatchNodebuildMap> filegroup in files)
			{
				if(worker != null && worker.CancellationPending) { result.Canceled = true; break; }
				List<BatchNodebuildMap> maps = filegroup.ToList();
				if(request.PreviewOnly)
				{
					try
					{
						byte[] hashbefore = HashFile(filegroup.Key);
						List<KeyValuePair<BatchNodebuildMap, BatchMapEditOutcome>> fileoutcomes = new List<KeyValuePair<BatchNodebuildMap, BatchMapEditOutcome>>();
						using(WAD wad = new WAD(filegroup.Key, true))
						{
							List<BatchNodebuildEngine.MapRange> ranges = BatchNodebuildEngine.DiscoverMapRanges(wad);
							foreach(BatchNodebuildMap map in maps)
							{
								if(worker != null && worker.CancellationPending) { result.Canceled = true; break; }
								BatchNodebuildEngine.MapRange range = FindRange(ranges, map);
								BatchMapEditOutcome outcome = AnalyzeMap(wad, range, request.Operation, false);
								fileoutcomes.Add(new KeyValuePair<BatchNodebuildMap, BatchMapEditOutcome>(map, outcome));
							}
						}
						if(result.Canceled) break;
						if(!HashesEqual(hashbefore, HashFile(filegroup.Key)))
							throw new IOException("The WAD changed while it was being previewed. Run Preview again.");
						result.FileHashes[filegroup.Key] = HashToString(hashbefore);
						foreach(KeyValuePair<BatchNodebuildMap, BatchMapEditOutcome> pair in fileoutcomes)
						{
							Accumulate(result, pair.Value);
							completed++;
							Report(worker, completed, request.Maps.Count, pair.Key,
								pair.Value.Matches + " match" + (pair.Value.Matches == 1 ? "" : "es") + ", " + pair.Value.Changes + " change" + (pair.Value.Changes == 1 ? "" : "s"), null);
						}
					}
					catch(Exception ex)
					{
						result.FilesFailed++;
						completed += maps.Count;
						ReportMany(worker, completed, request.Maps.Count, maps, "Preview failed", "Could not scan " + Path.GetFileName(filegroup.Key) + ": " + ex.Message);
					}
				}
				else
				{
					Dictionary<string, BatchMapEditOutcome> outcomes;
					string backup;
					string error;
					bool changed;
					bool canceled;
					if(ProcessFile(filegroup.Key, maps, request, worker, out outcomes, out changed, out backup, out error, out canceled))
					{
						foreach(BatchNodebuildMap map in maps)
						{
							BatchMapEditOutcome outcome = outcomes[map.Key];
							Accumulate(result, outcome);
							completed++;
							Report(worker, completed, request.Maps.Count, map, outcome.Changes > 0 ? "Edited" : "No change", null);
						}
						if(changed) result.FilesUpdated++; else result.FilesUnchanged++;
						Report(worker, completed, request.Maps.Count, null, null,
							(changed ? "Updated " + Path.GetFileName(filegroup.Key) + (string.IsNullOrEmpty(backup) ? "." : "; backup: " + backup)
							: Path.GetFileName(filegroup.Key) + " needed no data changes."));
					}
					else if(canceled)
					{
						result.Canceled = true;
						ReportMany(worker, completed, request.Maps.Count, maps, "Canceled", "Canceled " + Path.GetFileName(filegroup.Key) + "; original untouched.");
						break;
					}
					else
					{
						result.FilesFailed++;
						completed += maps.Count;
						ReportMany(worker, completed, request.Maps.Count, maps, "Failed; original safe",
							"Could not edit " + Path.GetFileName(filegroup.Key) + ": " + error + " The original WAD was not changed.");
					}
				}
			}

			return result;
		}

		private static void Accumulate(BatchMapEditResult result, BatchMapEditOutcome outcome)
		{
			result.MapsCompleted++;
			result.Matches += outcome.Matches;
			result.Changes += outcome.Changes;
			if(outcome.Matches > 0) result.MapsMatched++;
		}

		private static BatchNodebuildEngine.MapRange FindRange(List<BatchNodebuildEngine.MapRange> ranges, BatchNodebuildMap map)
		{
			BatchNodebuildEngine.MapRange range = ranges.FirstOrDefault(r => string.Equals(r.Name, map.MapName, StringComparison.OrdinalIgnoreCase));
			if(range == null) throw new InvalidDataException("Map " + map.MapName + " is no longer present.");
			if(range.IsUdmf != map.IsUdmf) throw new InvalidDataException("Map " + map.MapName + " changed format after it was added.");
			return range;
		}

		private static bool ProcessFile(string filepath, List<BatchNodebuildMap> maps, BatchMapEditRequest request,
			BackgroundWorker worker, out Dictionary<string, BatchMapEditOutcome> outcomes, out bool changed,
			out string backuppath, out string error, out bool canceled)
		{
			outcomes = new Dictionary<string, BatchMapEditOutcome>(StringComparer.OrdinalIgnoreCase);
			changed = false;
			backuppath = string.Empty;
			error = string.Empty;
			canceled = false;
			string stagingdirectory = string.Empty;
			try
			{
				if(!File.Exists(filepath)) throw new FileNotFoundException("The WAD no longer exists.", filepath);
				if((File.GetAttributes(filepath) & FileAttributes.ReadOnly) != 0) throw new InvalidOperationException("The WAD is marked read-only.");
				byte[] originalhash = HashFile(filepath);
				string expectedhash;
				if(request.ExpectedFileHashes == null || !request.ExpectedFileHashes.TryGetValue(filepath, out expectedhash)
					|| !string.Equals(expectedhash, HashToString(originalhash), StringComparison.OrdinalIgnoreCase))
					throw new IOException("The WAD changed after Preview. Run Preview again before applying edits.");
				string parent = Path.GetDirectoryName(filepath);
				stagingdirectory = Path.Combine(parent, ".uzb-batch-edit-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(stagingdirectory);
				try { File.SetAttributes(stagingdirectory, File.GetAttributes(stagingdirectory) | FileAttributes.Hidden); } catch(Exception) { }
				string stage = Path.Combine(stagingdirectory, Path.GetFileName(filepath));
				File.Copy(filepath, stage, true);

				bool stagehaschanges;
				using(WAD wad = new WAD(stage, false))
				{
					if(wad.IsIWAD) throw new InvalidOperationException("IWAD files are read-only game data and cannot be batch edited.");
					List<BatchNodebuildEngine.MapRange> ranges = BatchNodebuildEngine.DiscoverMapRanges(wad);
					foreach(BatchNodebuildMap map in maps)
					{
						if(worker != null && worker.CancellationPending) { canceled = true; return false; }
						BatchNodebuildEngine.MapRange range = FindRange(ranges, map);
						BatchMapEditOutcome outcome = AnalyzeMap(wad, range, request.Operation, true);
						outcomes[map.Key] = outcome;
					}
					stagehaschanges = outcomes.Values.Any(o => o.Changes > 0);
					if(stagehaschanges)
					{
						wad.WriteHeaders();
						wad.Flush();
						wad.Compress();
						wad.Flush();
					}
				}
				if(!stagehaschanges) return true;

				string verification;
				if(!OnlyExpectedMapLumpsChanged(filepath, stage, maps, request.Operation, out verification))
					throw new InvalidDataException("Safety check rejected the staged WAD: " + verification);
				if(!HashesEqual(originalhash, HashFile(filepath)))
					throw new IOException("The source WAD changed while the batch was running. Preview and apply again.");
				if(HashesEqual(originalhash, HashFile(stage))) return true;
				if(request.KeepBackups) backuppath = CreateUniqueBackupPath(filepath);
				File.Replace(stage, filepath, request.KeepBackups ? backuppath : null, true);
				changed = true;
				return true;
			}
			catch(Exception ex)
			{
				error = ex.Message;
				General.WriteLogLine("Batch map edit rejected " + filepath + ": " + ex.GetType().Name + ": " + ex.Message);
				return false;
			}
			finally
			{
				if(!string.IsNullOrEmpty(stagingdirectory) && Directory.Exists(stagingdirectory))
				{
					try { Directory.Delete(stagingdirectory, true); }
					catch(Exception ex) { General.WriteLogLine("Unable to remove batch edit staging directory: " + ex.Message); }
				}
			}
		}

		private static BatchMapEditOutcome AnalyzeMap(WAD wad, BatchNodebuildEngine.MapRange range,
			BatchMapEditOperation operation, bool apply)
		{
			if(range.IsUdmf)
			{
				int textmap = FindLump(wad, range, "TEXTMAP");
				byte[] original = ReadLump(wad.Lumps[textmap]);
				Encoding encoding = DetectTextEncoding(original);
				string text = encoding.GetString(RemoveBom(original, encoding));
				string edited;
				BatchMapEditOutcome outcome = BatchUdmfEditor.Edit(text, operation, out edited);
				if(apply && outcome.Changes > 0 && !string.Equals(text, edited, StringComparison.Ordinal))
					ReplaceLump(wad, textmap, AddBomIfPresent(encoding.GetBytes(edited), original, encoding));
				return outcome;
			}

			if(operation.Kind == BatchMapEditKind.Textures) return EditBinaryTextures(wad, range, operation, apply);
			return EditBinaryThings(wad, range, operation, apply);
		}

		private static BatchMapEditOutcome EditBinaryTextures(WAD wad, BatchNodebuildEngine.MapRange range,
			BatchMapEditOperation operation, bool apply)
		{
			if(operation.ReplaceTexture.Length > 8)
				throw new InvalidOperationException("Map " + range.Name + " is binary, so replacement texture names may not exceed 8 characters.");
			BatchMapEditOutcome outcome = new BatchMapEditOutcome();
			if(operation.TextureTop || operation.TextureMiddle || operation.TextureBottom)
			{
				int index = FindLump(wad, range, "SIDEDEFS");
				byte[] data = ReadLump(wad.Lumps[index]);
				if(data.Length % 30 != 0) throw new InvalidDataException("SIDEDEFS has an invalid record length in " + range.Name + ".");
				for(int pos = 0; pos < data.Length; pos += 30)
				{
					if(operation.TextureTop) ReplaceBinaryName(data, pos + 4, operation, outcome);
					if(operation.TextureBottom) ReplaceBinaryName(data, pos + 12, operation, outcome);
					if(operation.TextureMiddle) ReplaceBinaryName(data, pos + 20, operation, outcome);
				}
				if(apply && outcome.Changes > 0) ReplaceLump(wad, index, data);
			}

			if(operation.TextureFloor || operation.TextureCeiling)
			{
				int changesbefore = outcome.Changes;
				int index = FindLump(wad, range, "SECTORS");
				byte[] data = ReadLump(wad.Lumps[index]);
				if(data.Length % 26 != 0) throw new InvalidDataException("SECTORS has an invalid record length in " + range.Name + ".");
				for(int pos = 0; pos < data.Length; pos += 26)
				{
					if(operation.TextureFloor) ReplaceBinaryName(data, pos + 4, operation, outcome);
					if(operation.TextureCeiling) ReplaceBinaryName(data, pos + 12, operation, outcome);
				}
				if(apply && outcome.Changes > changesbefore) ReplaceLump(wad, index, data);
			}
			return outcome;
		}

		private static void ReplaceBinaryName(byte[] data, int offset, BatchMapEditOperation operation, BatchMapEditOutcome outcome)
		{
			string current = ReadName(data, offset);
			if(!string.Equals(current, operation.FindTexture, StringComparison.OrdinalIgnoreCase)) return;
			outcome.Matches++;
			if(string.Equals(current, operation.ReplaceTexture, StringComparison.Ordinal)) return;
			WriteName(data, offset, operation.ReplaceTexture);
			outcome.Changes++;
		}

		private static BatchMapEditOutcome EditBinaryThings(WAD wad, BatchNodebuildEngine.MapRange range,
			BatchMapEditOperation operation, bool apply)
		{
			int index = FindLump(wad, range, "THINGS");
			byte[] data = ReadLump(wad.Lumps[index]);
			bool hexen = HasLump(wad, range, "BEHAVIOR");
			int recordsize = hexen ? 20 : 10;
			if(data.Length % recordsize != 0) throw new InvalidDataException("THINGS has an invalid record length in " + range.Name + ".");
			if(operation.SetThingTag && !hexen)
				throw new InvalidOperationException("Map " + range.Name + " uses Doom-format binary Things, which do not store object tags. Disable Set tag or use UDMF/Hexen format.");

			BatchMapEditOutcome outcome = new BatchMapEditOutcome();
			for(int pos = 0; pos < data.Length; pos += recordsize)
			{
				int typeoffset = pos + (hexen ? 10 : 6);
				int type = ReadUInt16(data, typeoffset);
				if(!operation.ThingTypes.Contains(type)) continue;
				outcome.Matches++;
				bool changed = false;
				if(operation.SetThingType && type != operation.NewThingType) { WriteUInt16(data, typeoffset, operation.NewThingType); changed = true; }
				int angleoffset = pos + (hexen ? 8 : 4);
				if(operation.SetThingAngle && ReadUInt16(data, angleoffset) != operation.NewThingAngle) { WriteUInt16(data, angleoffset, operation.NewThingAngle); changed = true; }
				if(operation.SetThingTag && ReadUInt16(data, pos) != operation.NewThingTag) { WriteUInt16(data, pos, operation.NewThingTag); changed = true; }
				int flagoffset = pos + (hexen ? 12 : 8);
				int oldflags = ReadUInt16(data, flagoffset);
				int newflags = (oldflags | operation.EnableBinaryFlags) & ~operation.DisableBinaryFlags;
				newflags &= 65535;
				if(oldflags != newflags) { WriteUInt16(data, flagoffset, newflags); changed = true; }
				if(changed) outcome.Changes++;
			}
			if(apply && outcome.Changes > 0) ReplaceLump(wad, index, data);
			return outcome;
		}

		private static bool OnlyExpectedMapLumpsChanged(string original, string edited, List<BatchNodebuildMap> maps,
			BatchMapEditOperation operation, out string error)
		{
			using(WAD before = new WAD(original, true))
			using(WAD after = new WAD(edited, true))
			{
				if(before.Lumps.Count != after.Lumps.Count) { error = "the lump count changed."; return false; }
				List<BatchNodebuildEngine.MapRange> ranges = BatchNodebuildEngine.DiscoverMapRanges(before);
				HashSet<int> allowed = new HashSet<int>();
				foreach(BatchNodebuildMap map in maps)
				{
					BatchNodebuildEngine.MapRange range = FindRange(ranges, map);
					if(range.IsUdmf) allowed.Add(FindLump(before, range, "TEXTMAP"));
					else if(operation.Kind == BatchMapEditKind.Textures)
					{
						if(operation.TextureTop || operation.TextureMiddle || operation.TextureBottom) allowed.Add(FindLump(before, range, "SIDEDEFS"));
						if(operation.TextureFloor || operation.TextureCeiling) allowed.Add(FindLump(before, range, "SECTORS"));
					}
					else allowed.Add(FindLump(before, range, "THINGS"));
				}

				for(int i = 0; i < before.Lumps.Count; i++)
				{
					if(!string.Equals(before.Lumps[i].Name, after.Lumps[i].Name, StringComparison.Ordinal))
					{
						error = "lump " + i + " changed name or order.";
						return false;
					}
					if(allowed.Contains(i)) continue;
					byte[] first = ReadLump(before.Lumps[i]);
					byte[] second = ReadLump(after.Lumps[i]);
					if(!HashesEqual(HashBytes(first), HashBytes(second)))
					{
						error = "protected lump " + before.Lumps[i].Name + " at index " + i + " changed.";
						return false;
					}
				}
			}
			error = string.Empty;
			return true;
		}

		private static int FindLump(WAD wad, BatchNodebuildEngine.MapRange range, string name)
		{
			for(int i = range.StartIndex + 1; i <= range.EndIndex && i < wad.Lumps.Count; i++)
				if(string.Equals(wad.Lumps[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
			throw new InvalidDataException("Map " + range.Name + " is missing its " + name + " lump.");
		}

		private static bool HasLump(WAD wad, BatchNodebuildEngine.MapRange range, string name)
		{
			for(int i = range.StartIndex + 1; i <= range.EndIndex && i < wad.Lumps.Count; i++)
				if(string.Equals(wad.Lumps[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		private static byte[] ReadLump(Lump lump)
		{
			lump.Stream.Position = 0;
			byte[] data = new byte[lump.Length];
			int read = 0;
			while(read < data.Length)
			{
				int amount = lump.Stream.Read(data, read, data.Length - read);
				if(amount == 0) throw new EndOfStreamException("Unexpected end of lump " + lump.Name + ".");
				read += amount;
			}
			lump.Stream.Position = 0;
			return data;
		}

		private static void ReplaceLump(WAD wad, int index, byte[] data)
		{
			string name = wad.Lumps[index].Name;
			wad.RemoveAt(index, false);
			Lump replacement = wad.Insert(name, index, data.Length, false);
			replacement.Stream.Position = 0;
			replacement.Stream.Write(data, 0, data.Length);
			replacement.Stream.Position = 0;
		}

		private static string ReadName(byte[] data, int offset)
		{
			return WAD.ENCODING.GetString(data, offset, 8).TrimEnd('\0', ' ');
		}

		private static void WriteName(byte[] data, int offset, string value)
		{
			for(int i = 0; i < 8; i++) data[offset + i] = 0;
			byte[] encoded = WAD.ENCODING.GetBytes(value.ToUpperInvariant());
			Buffer.BlockCopy(encoded, 0, data, offset, Math.Min(8, encoded.Length));
		}

		private static int ReadUInt16(byte[] data, int offset) { return data[offset] | (data[offset + 1] << 8); }
		private static void WriteUInt16(byte[] data, int offset, int value) { data[offset] = (byte)(value & 255); data[offset + 1] = (byte)((value >> 8) & 255); }

		private static Encoding DetectTextEncoding(byte[] data)
		{
			if(data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return new UTF8Encoding(false, true);
			try { new UTF8Encoding(false, true).GetString(data); return new UTF8Encoding(false, true); }
			catch(DecoderFallbackException) { return WAD.ENCODING; }
		}

		private static byte[] RemoveBom(byte[] data, Encoding encoding)
		{
			if(encoding is UTF8Encoding && data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
				return data.Skip(3).ToArray();
			return data;
		}

		private static byte[] AddBomIfPresent(byte[] data, byte[] original, Encoding encoding)
		{
			if(encoding is UTF8Encoding && original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF)
				return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(data).ToArray();
			return data;
		}

		private static byte[] HashFile(string path)
		{
			using(SHA256 sha = SHA256.Create())
			using(FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return sha.ComputeHash(stream);
		}

		private static byte[] HashBytes(byte[] data) { using(SHA256 sha = SHA256.Create()) return sha.ComputeHash(data); }

		private static string HashToString(byte[] hash)
		{
			return BitConverter.ToString(hash).Replace("-", string.Empty);
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
			string basepath = filepath + ".before-batch-edit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string candidate = basepath + ".bak";
			int index = 2;
			while(File.Exists(candidate)) candidate = basepath + "-" + (index++).ToString(CultureInfo.InvariantCulture) + ".bak";
			return candidate;
		}

		private static void Report(BackgroundWorker worker, int completed, int total, BatchNodebuildMap map, string status, string message)
		{
			if(worker == null) return;
			worker.ReportProgress((int)Math.Round(completed * 100.0 / Math.Max(1, total)), new BatchMapEditProgress
			{
				MapKey = map == null ? null : map.Key,
				MapStatus = status,
				StatusText = "Processed " + completed + " of " + total + " maps",
				LogText = message
			});
		}

		private static void ReportMany(BackgroundWorker worker, int completed, int total, IEnumerable<BatchNodebuildMap> maps, string status, string message)
		{
			bool first = true;
			foreach(BatchNodebuildMap map in maps)
			{
				Report(worker, completed, total, map, status, first ? message : null);
				first = false;
			}
		}
	}

	internal static class BatchUdmfEditor
	{
		private sealed class Token
		{
			internal string Text;
			internal int Start;
			internal int Length;
		}

		private sealed class Block
		{
			internal string Name;
			internal int CloseBrace;
			internal Dictionary<string, List<Token>> Properties;
		}

		private sealed class TextEdit
		{
			internal int Start;
			internal int Length;
			internal string Value;
		}

		internal static BatchMapEditOutcome Edit(string text, BatchMapEditOperation operation, out string edited)
		{
			List<Token> tokens = Tokenize(text);
			List<Block> blocks = FindBlocks(tokens);
			List<TextEdit> edits = new List<TextEdit>();
			BatchMapEditOutcome outcome = new BatchMapEditOutcome();
			if(operation.Kind == BatchMapEditKind.Textures)
			{
				foreach(Block block in blocks)
				{
					if(string.Equals(block.Name, "sidedef", StringComparison.OrdinalIgnoreCase))
					{
						if(operation.TextureTop) ReplaceTexture(block, "texturetop", operation, edits, outcome);
						if(operation.TextureMiddle) ReplaceTexture(block, "texturemiddle", operation, edits, outcome);
						if(operation.TextureBottom) ReplaceTexture(block, "texturebottom", operation, edits, outcome);
					}
					else if(string.Equals(block.Name, "sector", StringComparison.OrdinalIgnoreCase))
					{
						if(operation.TextureFloor) ReplaceTexture(block, "texturefloor", operation, edits, outcome);
						if(operation.TextureCeiling) ReplaceTexture(block, "textureceiling", operation, edits, outcome);
					}
				}
			}
			else
			{
				foreach(Block block in blocks)
				{
					if(!string.Equals(block.Name, "thing", StringComparison.OrdinalIgnoreCase)) continue;
					Token typetoken = GetLast(block, "type");
					int type;
					if(typetoken == null || !int.TryParse(Unquote(typetoken.Text), NumberStyles.Integer, CultureInfo.InvariantCulture, out type)
						|| !operation.ThingTypes.Contains(type)) continue;
					outcome.Matches++;
					Dictionary<string, string> desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					if(operation.SetThingType) desired["type"] = operation.NewThingType.ToString(CultureInfo.InvariantCulture);
					if(operation.SetThingTag) desired["id"] = operation.NewThingTag.ToString(CultureInfo.InvariantCulture);
					if(operation.SetThingAngle) desired["angle"] = operation.NewThingAngle.ToString(CultureInfo.InvariantCulture);
					foreach(string flag in operation.EnableUdmfFlags) desired[flag] = "true";
					foreach(string flag in operation.DisableUdmfFlags) desired[flag] = "false";
					bool changed = ApplyDesiredProperties(text, block, desired, edits);
					if(changed) outcome.Changes++;
				}
			}

			edited = ApplyEdits(text, edits);
			return outcome;
		}

		private static void ReplaceTexture(Block block, string property, BatchMapEditOperation operation,
			List<TextEdit> edits, BatchMapEditOutcome outcome)
		{
			List<Token> values;
			if(!block.Properties.TryGetValue(property, out values)) return;
			foreach(Token value in values)
			{
				if(!string.Equals(Unquote(value.Text), operation.FindTexture, StringComparison.OrdinalIgnoreCase)) continue;
				outcome.Matches++;
				edits.Add(new TextEdit { Start = value.Start, Length = value.Length, Value = Quote(operation.ReplaceTexture) });
				outcome.Changes++;
			}
		}

		private static bool ApplyDesiredProperties(string source, Block block, Dictionary<string, string> desired, List<TextEdit> edits)
		{
			bool changed = false;
			List<KeyValuePair<string, string>> missing = new List<KeyValuePair<string, string>>();
			foreach(KeyValuePair<string, string> pair in desired)
			{
				List<Token> values;
				if(block.Properties.TryGetValue(pair.Key, out values))
				{
					foreach(Token value in values)
					{
						if(string.Equals(value.Text, pair.Value, StringComparison.OrdinalIgnoreCase)) continue;
						edits.Add(new TextEdit { Start = value.Start, Length = value.Length, Value = pair.Value });
						changed = true;
					}
				}
				else
				{
					missing.Add(pair);
					changed = true;
				}
			}

			if(missing.Count > 0)
			{
				string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
				int linestart = block.CloseBrace;
				while(linestart > 0 && source[linestart - 1] != '\n' && source[linestart - 1] != '\r') linestart--;
				string beforeclose = source.Substring(linestart, block.CloseBrace - linestart);
				int indentlength = 0;
				while(indentlength < beforeclose.Length && (beforeclose[indentlength] == ' ' || beforeclose[indentlength] == '\t')) indentlength++;
				string lineindent = beforeclose.Substring(0, indentlength);
				bool closeonownline = beforeclose.Trim().Length == 0;
				string propertyindent = lineindent + "\t";
				StringBuilder insertion = new StringBuilder();
				if(!closeonownline) insertion.Append(newline);
				foreach(KeyValuePair<string, string> pair in missing)
					insertion.Append(propertyindent).Append(pair.Key).Append(" = ").Append(pair.Value).Append(';').Append(newline);
				if(!closeonownline) insertion.Append(lineindent);
				edits.Add(new TextEdit { Start = closeonownline ? linestart : block.CloseBrace, Length = 0, Value = insertion.ToString() });
			}
			return changed;
		}

		private static Token GetLast(Block block, string property)
		{
			List<Token> values;
			return block.Properties.TryGetValue(property, out values) && values.Count > 0 ? values[values.Count - 1] : null;
		}

		private static string ApplyEdits(string source, List<TextEdit> edits)
		{
			StringBuilder result = new StringBuilder(source);
			foreach(TextEdit edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
			{
				result.Remove(edit.Start, edit.Length);
				result.Insert(edit.Start, edit.Value);
			}
			return result.ToString();
		}

		private static List<Block> FindBlocks(List<Token> tokens)
		{
			List<Block> result = new List<Block>();
			for(int i = 0; i + 1 < tokens.Count; i++)
			{
				if(tokens[i + 1].Text != "{" || !IsIdentifier(tokens[i].Text)) continue;
				int depth = 1;
				int close = -1;
				for(int p = i + 2; p < tokens.Count; p++)
				{
					if(tokens[p].Text == "{") depth++;
					else if(tokens[p].Text == "}" && --depth == 0) { close = p; break; }
				}
				if(close < 0) throw new InvalidDataException("TEXTMAP contains an unterminated " + tokens[i].Text + " block.");
				Block block = new Block();
				block.Name = tokens[i].Text;
				block.CloseBrace = tokens[close].Start;
				block.Properties = new Dictionary<string, List<Token>>(StringComparer.OrdinalIgnoreCase);
				int innerdepth = 0;
				for(int p = i + 2; p + 3 < close; p++)
				{
					if(tokens[p].Text == "{") { innerdepth++; continue; }
					if(tokens[p].Text == "}") { innerdepth--; continue; }
					if(innerdepth != 0 || !IsIdentifier(tokens[p].Text) || tokens[p + 1].Text != "=" || tokens[p + 3].Text != ";") continue;
					List<Token> values;
					if(!block.Properties.TryGetValue(tokens[p].Text, out values))
					{
						values = new List<Token>();
						block.Properties.Add(tokens[p].Text, values);
					}
					values.Add(tokens[p + 2]);
					p += 3;
				}
				result.Add(block);
				i = close;
			}
			return result;
		}

		private static List<Token> Tokenize(string text)
		{
			List<Token> result = new List<Token>();
			int i = 0;
			while(i < text.Length)
			{
				char c = text[i];
				if(char.IsWhiteSpace(c)) { i++; continue; }
				if(c == '/' && i + 1 < text.Length && text[i + 1] == '/')
				{
					i += 2;
					while(i < text.Length && text[i] != '\r' && text[i] != '\n') i++;
					continue;
				}
				if(c == '/' && i + 1 < text.Length && text[i + 1] == '*')
				{
					i += 2;
					while(i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
					i = Math.Min(text.Length, i + 2);
					continue;
				}
				int start = i;
				if(c == '"')
				{
					i++;
					while(i < text.Length)
					{
						if(text[i] == '\\' && i + 1 < text.Length) { i += 2; continue; }
						if(text[i++] == '"') break;
					}
				}
				else if(c == '{' || c == '}' || c == '=' || c == ';') i++;
				else
				{
					while(i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '=' && text[i] != ';')
					{
						if(text[i] == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*')) break;
						i++;
					}
				}
				if(i == start) { i++; continue; }
				result.Add(new Token { Text = text.Substring(start, i - start), Start = start, Length = i - start });
			}
			return result;
		}

		internal static bool IsIdentifier(string value)
		{
			if(string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
			for(int i = 1; i < value.Length; i++) if(!(char.IsLetterOrDigit(value[i]) || value[i] == '_')) return false;
			return true;
		}

		private static string Unquote(string value)
		{
			if(value == null || value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"') return value;
			StringBuilder result = new StringBuilder(value.Length - 2);
			for(int i = 1; i < value.Length - 1; i++)
			{
				if(value[i] == '\\' && i + 1 < value.Length - 1) result.Append(value[++i]);
				else result.Append(value[i]);
			}
			return result.ToString();
		}

		private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
	}
}
