#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	public class ResultVertexOverlappingLine : ErrorResult
	{
		#region ================== Variables

		private readonly Linedef line;
		private readonly Vertex vertex;

		#endregion

		#region ================== Properties

		public override int Buttons { get { return 1; } }
		public override string Button1Text { get { return "Split Linedef"; } }
		
		#endregion
		
		#region ================== Constructor / Destructor
		
		// Constructor
		public ResultVertexOverlappingLine(Vertex v, Linedef l)
		{
			// Initialize
			line = l;
			vertex = v;
			viewobjects.Add(l);
			viewobjects.Add(v);
			hidden = (l.IgnoredErrorChecks.Contains(this.GetType()) && v.IgnoredErrorChecks.Contains(this.GetType())); //mxd
			description = "This vertex overlaps this linedef without splitting it.";
		}

		#endregion

		#region ================== Methods

		// This sets if this result is displayed in ErrorCheckForm (mxd)
		internal override void Hide(bool hide) 
		{
			hidden = hide;
			Type t = this.GetType();
			if(hide) 
			{
				vertex.IgnoredErrorChecks.Add(t);
				line.IgnoredErrorChecks.Add(t);
			} 
			else 
			{
				if(vertex.IgnoredErrorChecks.Contains(t)) vertex.IgnoredErrorChecks.Remove(t);
				if(line.IgnoredErrorChecks.Contains(t)) line.IgnoredErrorChecks.Remove(t);
			}
		}

		// This must return the string that is displayed in the listbox
		public override string ToString() 
		{
			return "Vertex " + vertex.Index + " overlaps line " + line.Index + " without splitting it";
		}

		// Rendering
		public override void PlotSelection(IRenderer2D renderer) 
		{
			renderer.PlotLinedef(line, General.Colors.Selection);
			renderer.PlotVertex(line.Start, ColorCollection.VERTICES);
			renderer.PlotVertex(line.End, ColorCollection.VERTICES);

			renderer.PlotVertex(vertex, ColorCollection.SELECTION);
		}

		// Fix by splitting the line
		public override bool Button1Click(bool batchMode) 
		{
			// Earlier fixes in a batch can split or join this result's original
			// linedef. Resolve the live descendant that still contains the vertex
			// instead of dereferencing the stale result object (GitLab #79).
			if(vertex == null || vertex.IsDisposed) return true;
			Linedef targetline = FindCurrentLine();
			if(targetline == null) return true; // Already fixed by another batch item.

			if(!batchMode) General.Map.UndoRedo.CreateUndo("Split Linedef");
			if(targetline.Split(vertex) == null) return false;

			//check that we don't have duplicate lines
			List<Linedef> lines = new List<Linedef>(vertex.Linedefs);

			for(int i = 0; i < lines.Count - 1; i++) 
			{
				for(int c = i + 1; c < lines.Count; c++) 
				{
					Linedef survivor = lines[i];
					Linedef duplicate = lines[c];
					if(survivor == null || duplicate == null || survivor.IsDisposed || duplicate.IsDisposed) continue;

					if((survivor.Start == duplicate.Start && survivor.End == duplicate.End) ||
						(survivor.Start == duplicate.End && survivor.End == duplicate.Start))
					{
						if(!duplicate.Join(survivor)) continue;

						//mxd. Textures may've become unused
						if(survivor.Front != null) survivor.Front.RemoveUnneededTextures(survivor.Back != null, false, true);
						if(survivor.Back != null) survivor.Back.RemoveUnneededTextures(survivor.Front != null, false, true);
					}
				}
			}

			General.Map.Map.Update();
			return true;
		}

		private Linedef FindCurrentLine()
		{
			if(IsInteriorOverlap(line)) return line;

			foreach(Linedef candidate in General.Map.Map.Linedefs)
				if(IsInteriorOverlap(candidate)) return candidate;

			return null;
		}

		private bool IsInteriorOverlap(Linedef candidate)
		{
			if(candidate == null || candidate.IsDisposed || candidate.Start == null || candidate.End == null) return false;
			if(candidate.Start == vertex || candidate.End == vertex) return false;

			double u = Line2D.GetNearestOnLine(candidate.Start.Position, candidate.End.Position, vertex.Position);
			if(u <= 0.0000001 || u >= 0.9999999) return false;

			return Line2D.GetDistanceToLineSq(candidate.Start.Position, candidate.End.Position,
				vertex.Position, true) < 0.00000025;
		}
		
		#endregion
	}
}
