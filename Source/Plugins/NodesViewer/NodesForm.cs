#region === Copyright (c) 2010 Pascal van der Heiden ===

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.Plugins.NodesViewer
{
	public partial class NodesForm : DelayedForm
	{
		#region ================== Variables

		private NodesViewerMode mode;
		private ToolTip metricstooltip;

		#endregion

		#region ================== Properties

		public int SelectedTab { get { return tabs.SelectedIndex; } }
		public int ViewSplitIndex { get { return (int)splitindex.Value; } }
		public int ViewSubsectorIndex { get { return (int)ssectorindex.Value; } }
		public int ViewSegIndex { get { return viewsegbox.Checked ? (int)segindex.Value : -1; } }
		public bool ShowSegsVertices { get { return showsegsvertices.Checked; } }

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public NodesForm()
		{
			InitializeComponent();
			ConfigureTooltips();
		}

		// Constructor
		public NodesForm(NodesViewerMode mode)
		{
			InitializeComponent();
			ConfigureTooltips();
			this.mode = mode;

			// Counts
			numsegs.Text = mode.Segs.Length.ToString();
			numsplits.Text = mode.Nodes.Length.ToString();
			numssectors.Text = mode.Subsectors.Length.ToString();
			numvertices.Text = mode.Vertices.Length.ToString();
			showsegsvertices.Text = "Show additional vertices (" + (mode.Vertices.Length - General.Map.Map.Vertices.Count) + " seg splits)";

			// Create stats on the tree
			List<int> leafdepths = new List<int>();
			DiveTree(mode.Nodes.Length - 1, 0, leafdepths);
			int maxdepth = 0;
			double totaldepth = 0.0;
			foreach(int d in leafdepths)
			{
				if(d > maxdepth) maxdepth = d;
				totaldepth += d;
			}

			treedepth.Text = maxdepth.ToString();

			// Compare average traversal depth with the theoretical depth of a perfectly
			// balanced binary tree. The old min/max ratio made one isolated shallow leaf
			// label almost every real-world map as badly uneven.
			double averagedepth = totaldepth / leafdepths.Count;
			double idealdepth = Math.Max(1.0, Math.Log(leafdepths.Count, 2.0));
			int balance = (int)Math.Round(Math.Min(1.0, idealdepth / averagedepth) * 100.0);
			treebalance.Text = balance + "%";
			if(balance < 70)
			{
				label11.Text = "This tree has unusually expensive average traversal. This is diagnostic information only: BSP balance does not combine, remove, or alter your map sectors. Splits are generated and read-only.";
			}

			// Start viewing root split
			splitindex.Maximum = mode.Nodes.Length - 1;
			splitindex.Value = mode.Nodes.Length - 1;
			splitindex_ValueChanged(null, EventArgs.Empty);
			
			// Viewing subsector
			ssectorindex.Maximum = mode.Subsectors.Length - 1;
			ssectorindex.Value = 0;
			ssectorindex_ValueChanged(null, EventArgs.Empty);
			segindex_ValueChanged(null, EventArgs.Empty);
		}

		// Clean up any resources being used.
		protected override void Dispose(bool disposing)
		{
			if(disposing && metricstooltip != null)
			{
				metricstooltip.Dispose();
				metricstooltip = null;
			}
			if(disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#endregion

		#region ================== Methods

		private void ConfigureTooltips()
		{
			metricstooltip = new ToolTip
			{
				AutoPopDelay = 12000,
				InitialDelay = 350,
				ReshowDelay = 100,
				ShowAlways = true
			};

			const string splitshelp = "A BSP split divides the current generated region into two child regions. The colored left/right areas are node-builder bounds, not map sectors, and long partition lines are only a visualization. Individual splits cannot be dragged or saved because every node rebuild regenerates the complete tree. A different node-builder profile can influence the overall heuristic, but not safely place each split by hand. More splits increase node data, build time, and memory slightly; the number alone does not predict FPS.";
			SetMetricHelp(label2, numsplits, splitshelp);
			SetMetricHelp(label10, treedepth,
				"Depth is the largest number of partition decisions needed to reach any subsector. Very high depth can make worst-case visibility and collision traversal slower, but a single deep branch does not mean the whole map is slow.");
			SetMetricHelp(label6, treebalance,
				"Balance estimates how efficiently the BSP structure reaches the map's subsectors. Closer to 100% generally means fewer split decisions. Low values may increase CPU work, but do not change the map's appearance or correctness.");
			SetMetricHelp(label4, numvertices,
				"Vertices includes the map's original points plus extra points created where the node builder splits lines. More generated vertices increase node size and loading slightly; ordinary counts have little direct gameplay impact.");
			SetMetricHelp(label3, numsegs,
				"Segs are linedef fragments stored in subsectors. More segs use more memory and can add traversal/render preparation work. Curves naturally produce more, so lower is not always better if it creates a much deeper tree.");
			SetMetricHelp(label1, numssectors,
				"Subsectors are convex leaf regions produced by the node builder. The engine uses them for visibility and rendering. More subsectors increase node data; their layout and traversal depth matter more than the raw count.");
			BindHelp(showsegsvertices,
				"Displays node-builder-created split points. This changes only the viewer overlay and has no effect on the map or in-game performance. The overview outlines the real generated subsectors; it does not draw the internal triangles used only to fill those shapes on screen.");
			BindHelp(buildnodesbutton,
				"Rebuild nodes: runs the currently selected Save node-builder profile one time. It replaces generated BSP data, then reloads this viewer. It does not move linedefs, sectors, things, or textures. The safe Normal profile may choose different splits after geometry changes, but individual splits are not editable.");
			BindHelp(label5, splitshelp);
			BindHelp(splitindex, splitshelp);
			BindHelp(label16, splitshelp);
			BindHelp(parentsplit, splitshelp);
			BindHelp(groupBox1, splitshelp);
			BindHelp(groupBox2, splitshelp);
			BindHelp(label7, splitshelp);
			BindHelp(leftarea, splitshelp);
			BindHelp(label15, splitshelp);
			BindHelp(rightarea, splitshelp);
		}

		private void SetMetricHelp(Control caption, Control value, string text)
		{
			BindHelp(caption, text);
			BindHelp(value, text);
			caption.Cursor = Cursors.Help;
			value.Cursor = Cursors.Help;
		}

		private void BindHelp(Control control, string text)
		{
			metricstooltip.SetToolTip(control, text);
			control.MouseEnter += delegate { label11.Text = text; };
			control.Enter += delegate { label11.Text = text; };
			control.MouseDown += delegate { label11.Text = text; };
		}

		// This calculates tree depths iteratively so malformed or extremely deep
		// maps cannot overflow the UI thread while the viewer form is opening.
		private void DiveTree(int node, int level, List<int> leafdepths)
		{
			Stack<KeyValuePair<int, int>> pending = new Stack<KeyValuePair<int, int>>();
			pending.Push(new KeyValuePair<int, int>(node, level));
			while(pending.Count > 0)
			{
				KeyValuePair<int, int> current = pending.Pop();
				Node currentnode = mode.Nodes[current.Key];
				int childlevel = current.Value + 1;

				if(currentnode.leftsubsector) leafdepths.Add(childlevel);
				else pending.Push(new KeyValuePair<int, int>(currentnode.leftchild, childlevel));

				if(currentnode.rightsubsector) leafdepths.Add(childlevel);
				else pending.Push(new KeyValuePair<int, int>(currentnode.rightchild, childlevel));
			}
		}

		// Show this form
		public void Show(Form owner)
		{
			// Show window
			base.Show(owner);
		}

		// Switch to a subsector
		public void ShowSubsector(int ssindex)
		{
			ssectorindex.Value = ssindex;
			tabs.SelectTab(tabsubsectors);
		}

		#endregion

		#region ================== Form / Overview Events

		// Exit this mode
		private void closebutton_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		// Exit mode when dialog is closed
		private void NodesForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			General.Editing.CancelMode();
		}

		// Open tab changed
		private void tabs_SelectedIndexChanged(object sender, EventArgs e)
		{
			General.Interface.RedrawDisplay();
		}

		// Show segs vertices changed
		private void showsegsvertices_CheckedChanged(object sender, EventArgs e)
		{
			General.Interface.RedrawDisplay();
		}

		// (Re)build the nodes
		private void buildnodesbutton_Click(object sender, EventArgs e)
		{
			label11.Text = "Rebuilding nodes now using the current Save profile. This replaces only generated BSP data and does not move or edit your map geometry.";
			label11.Refresh();
			// This button is an explicit edit to the generated lumps. Keep the map
			// dirty until the user saves those rebuilt nodes into the actual WAD.
			if(!General.Map.RebuildNodes(General.Map.ConfigSettings.NodebuilderSave, true, true)) return;
			bool showvertices = this.showsegsvertices.Checked;
			Point formlocation = this.Location;
			Size formsize = this.Size;

			// Restart the mode so that the new structures are loaded in.
			// This will automatically close and re-open this window.
			General.Editing.CancelMode();
			NodesViewerMode newmode = new NodesViewerMode();
			General.Editing.ChangeMode(newmode);

			// If something went wrong while engaging the mode (for example an unsupported node format was detected)
			// the mode will be disposed, so we need to check for it here
			if (!newmode.IsDisposed)
			{
				newmode.Form.showsegsvertices.Checked = showvertices;
				newmode.Form.Location = formlocation; //mxd
				newmode.Form.Size = formsize;
			}
		}

		#endregion

		#region ================== Splits Events

		// Go to the root split
		private void rootbutton_Click(object sender, EventArgs e)
		{
			splitindex.Value = splitindex.Maximum;
		}

		// Go to the parent split
		private void parentbutton_Click(object sender, EventArgs e)
		{
			Node n = mode.Nodes[(int)splitindex.Value];
			if(n.parent > -1) splitindex.Value = n.parent;
		}

		// Go to the left split/subsector
		private void leftbutton_Click(object sender, EventArgs e)
		{
			Node n = mode.Nodes[(int)splitindex.Value];
			if(n.leftsubsector)
				ShowSubsector(n.leftchild);
			else
				splitindex.Value = n.leftchild;
		}

		// Go to the right split/subsector
		private void rightbutton_Click(object sender, EventArgs e)
		{
			Node n = mode.Nodes[(int)splitindex.Value];
			if(n.rightsubsector)
				ShowSubsector(n.rightchild);
			else
				splitindex.Value = n.rightchild;
		}

		// Split changes
		private void splitindex_ValueChanged(object sender, EventArgs e)
		{
			Node n = mode.Nodes[(int)splitindex.Value];
			if(n.parent == -1)
			{
				parentsplit.Text = "(root split)";
				parentsplit.Enabled = false;
				parentbutton.Enabled = false;
			}
			else
			{
				parentsplit.Text = n.parent.ToString();
				parentsplit.Enabled = true;
				parentbutton.Enabled = true;
			}

			leftarea.Text = "(" + n.leftbox.Left + ", " + n.leftbox.Top + ") - (" + n.leftbox.Right + ", " + n.leftbox.Bottom + ")";
			rightarea.Text = "(" + n.rightbox.Left + ", " + n.rightbox.Top + ") - (" + n.rightbox.Right + ", " + n.rightbox.Bottom + ")";
			leftindex.Text = n.leftchild.ToString();
			rightindex.Text = n.rightchild.ToString();

			if(n.leftsubsector)
			{
				lefttype.Text = "Subsector:";
				leftbutton.Text = "Go to subsector";
			}
			else
			{
				lefttype.Text = "Split:";
				leftbutton.Text = "Go to split";
			}

			if(n.rightsubsector)
			{
				righttype.Text = "Subsector:";
				rightbutton.Text = "Go to subsector";
			}
			else
			{
				righttype.Text = "Split:";
				rightbutton.Text = "Go to split";
			}

			General.Interface.RedrawDisplay();
		}

		#endregion

		#region ================== Subsectors Events

		// Go to parent split for this subsector
		private void ssparentsplit_Click(object sender, EventArgs e)
		{
			// Find parent split
			int ss = (int)ssectorindex.Value;
			int parentsplit = -1;
			for(int i = 0; i < mode.Nodes.Length; i++)
			{
				Node n = mode.Nodes[i];
				if((n.leftsubsector && (n.leftchild == ss)) || (n.rightsubsector && (n.rightchild == ss)))
				{
					parentsplit = i;
					break;
				}
			}
			if(parentsplit == -1) return;
			splitindex.Value = parentsplit;
			tabs.SelectTab(tabsplits);
		}

		// Subsector changes
		private void ssectorindex_ValueChanged(object sender, EventArgs e)
		{
			Subsector s = mode.Subsectors[(int)ssectorindex.Value];
			
			ssectornumsegs.Text = s.numsegs.ToString();
			segindex.Minimum = s.firstseg;
			segindex.Maximum = s.firstseg + s.numsegs - 1;
			segindex.Value = s.firstseg;
			
			General.Interface.RedrawDisplay();
		}

		// Segment changes
		private void segindex_ValueChanged(object sender, EventArgs e)
		{
			Seg sg = mode.Segs[(int)segindex.Value];
			Linedef ld = null; //mxd
			if(sg.lineindex != -1) ld = General.Map.Map.GetLinedefByIndex(sg.lineindex); //mxd

			lineindex.Text = sg.lineindex.ToString();
			startvertex.Text = sg.startvertex + "  (" + mode.Vertices[sg.startvertex].x + ", " + mode.Vertices[sg.startvertex].y + ")";
			endvertex.Text = sg.endvertex + "  (" + mode.Vertices[sg.endvertex].x + ", " + mode.Vertices[sg.endvertex].y + ")";
			segside.Text = sg.leftside ? "Back" : "Front";
			segangle.Text = Angle2D.RealToDoom(sg.angle) + "\u00B0";
			segoffset.Text = sg.offset + " mp";

			if(ld != null) //mxd
			{ 
				sideindex.Text = sg.leftside ? ld.Back.Index.ToString() : ld.Front.Index.ToString();
				sectorindex.Text = sg.leftside ? ld.Back.Sector.Index.ToString() : ld.Front.Sector.Index.ToString();
			}
			else
			{
				sideindex.Text = "None";
				sectorindex.Text = "None";
			}

			General.Interface.RedrawDisplay();
		}

		// View segment checkbox changes
		private void viewsegbox_CheckedChanged(object sender, EventArgs e)
		{
			General.Interface.RedrawDisplay();
		}

		#endregion
	}
}
