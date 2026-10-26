using System.Drawing;
using CodeImp.DoomBuilder.Map;

namespace CodeImp.DoomBuilder.BuilderModes
{
	// Keeps one faulty analysis module from taking down the editor and makes the
	// incomplete check visible instead of silently presenting "no errors".
	public sealed class ResultCheckerFailed : ErrorResult
	{
		private readonly string checkername;

		public ResultCheckerFailed(string checkername, string error)
		{
			this.checkername = checkername;
			description = "The \"" + checkername + "\" analysis could not complete. Other enabled checks continued. "
				+ "Details were written to the UZB log.\r\n\r\n" + error;
		}

		internal override void Hide(bool hide) { }

		public override string ToString()
		{
			return "Analysis check failed: " + checkername;
		}

		public override RectangleF GetZoomArea()
		{
			if(General.Map == null || General.Map.Map == null || General.Map.Map.Vertices.Count == 0)
				return new RectangleF(-64f, -64f, 128f, 128f);

			RectangleF area = MapSet.CreateArea(General.Map.Map.Vertices);
			if(area.Width < 1f) area.Width = 1f;
			if(area.Height < 1f) area.Height = 1f;
			return area;
		}
	}
}
