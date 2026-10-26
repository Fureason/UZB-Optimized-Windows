using System.Drawing;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.Controls
{
	public partial class StatisticsControl : UserControl
	{
		private int lastvertices = -1;
		private int lastlinedefs = -1;
		private int lastsidedefs = -1;
		private int lastsectors = -1;
		private int lastthings = -1;

		public StatisticsControl() 
		{
			InitializeComponent();
			this.Visible = false;
		}

		public void UpdateStatistics()
		{
			int vertices = General.Map.Map.Vertices.Count;
			int linedefs = General.Map.Map.Linedefs.Count;
			int sidedefs = General.Map.Map.Sidedefs.Count;
			int sectors = General.Map.Map.Sectors.Count;
			int things = General.Map.Map.Things.Count;
			if(vertices == lastvertices && linedefs == lastlinedefs && sidedefs == lastsidedefs
				&& sectors == lastsectors && things == lastthings) return;

			lastvertices = vertices;
			lastlinedefs = linedefs;
			lastsidedefs = sidedefs;
			lastsectors = sectors;
			lastthings = things;

			// Update statistics
			verticescount.Text = vertices.ToString();
			linedefscount.Text = linedefs.ToString();
			sidedefscount.Text = sidedefs.ToString();
			sectorscount.Text = sectors.ToString();
			thingscount.Text = things.ToString();

			// Exceeding them limits?
			verticescount.ForeColor = (General.Map.Map.Vertices.Count > General.Map.FormatInterface.MaxVertices ? Color.Red : SystemColors.GrayText);
			linedefscount.ForeColor = (General.Map.Map.Linedefs.Count > General.Map.FormatInterface.MaxLinedefs ? Color.Red : SystemColors.GrayText);
			sidedefscount.ForeColor = (General.Map.Map.Sidedefs.Count > General.Map.FormatInterface.MaxSidedefs ? Color.Red : SystemColors.GrayText);
			sectorscount.ForeColor = (General.Map.Map.Sectors.Count > General.Map.FormatInterface.MaxSectors ? Color.Red : SystemColors.GrayText);
			thingscount.ForeColor = (General.Map.Map.Things.Count > General.Map.FormatInterface.MaxThings ? Color.Red : SystemColors.GrayText);

			verticeslabel.ForeColor = verticescount.ForeColor;
			linedefslabel.ForeColor = linedefscount.ForeColor;
			sidedefslabel.ForeColor = sidedefscount.ForeColor;
			sectorslabel.ForeColor = sectorscount.ForeColor;
			thingslabel.ForeColor = thingscount.ForeColor;
		}
	}
}
