#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.VisualModes;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	[ErrorChecker("Check texture alignment", false, 1000)]
	public class CheckTextureAlignment : ErrorChecker
	{
		#region ================== Constants

		private const int PROGRESS_STEP = 100;

		#endregion

		#region ================== Variables

		// Pairs already compared. The former four-level dictionary created several
		// small dictionaries for every sidedef part, which became expensive on maps
		// with many split walls.
		private HashSet<AlignmentPair> donesides;

		private struct AlignmentPair : IEquatable<AlignmentPair>
		{
			private readonly long first;
			private readonly long second;

			internal AlignmentPair(Sidedef side1, VisualGeometryType type1, Sidedef side2, VisualGeometryType type2)
			{
				long key1 = ((long)side1.Index << 8) | (byte)type1;
				long key2 = ((long)side2.Index << 8) | (byte)type2;
				if(key1 <= key2) { first = key1; second = key2; }
				else { first = key2; second = key1; }
			}

			public bool Equals(AlignmentPair other) { return first == other.first && second == other.second; }
			public override bool Equals(object obj) { return obj is AlignmentPair && Equals((AlignmentPair)obj); }
			public override int GetHashCode()
			{
				unchecked { return ((int)first * 397) ^ (int)(first >> 32) ^ (int)second ^ (int)(second >> 32); }
			}
		}

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public CheckTextureAlignment()
		{
			// Total progress is done when all lines are checked
			SetTotalProgress(General.Map.Map.Linedefs.Count / PROGRESS_STEP);
		}
		
		#endregion
		
		#region ================== Methods
		
		// This runs the check
		public override void Run()
		{
			donesides = new HashSet<AlignmentPair>();
			
			int progress = 0;
			int stepprogress = 0;

			// Go for all the liendefs
			foreach(Linedef l in General.Map.Map.Linedefs) 
			{
				// Check if we need to align any part of the front sidedef
				if(l.Front != null) 
				{
					CheckTopAlignment(l.Front);
					CheckMiddleAlignment(l.Front);
					CheckBottomAlignment(l.Front);
				}

				// Check if we need to align any part of the back sidedef
				if(l.Back != null) 
				{
					CheckTopAlignment(l.Back);
					CheckMiddleAlignment(l.Back);
					CheckBottomAlignment(l.Back);
				}

				// Handle thread interruption
				try
				{
					Thread.Sleep(0);
				}
				catch(ThreadInterruptedException)
				{
					// Clear collection
					donesides.Clear();
					return;
				}

				// We are making progress!
				if((++progress / PROGRESS_STEP) > stepprogress) 
				{
					stepprogress = (progress / PROGRESS_STEP);
					AddProgress(1);
				}
			}

			// Clear collection
			donesides.Clear();
		}

		#endregion

		#region ================== Alignment checks

		private void CheckTopAlignment(Sidedef sidedef) 
		{
			if(!sidedef.HighRequired() || sidedef.LongHighTexture == MapSet.EmptyLongName) return;
			
			double scaleX = sidedef.Fields.GetValue("scalex_top", 1.0);
			double scaleY = sidedef.Fields.GetValue("scaley_top", 1.0);
			int localY = (int)Math.Round(sidedef.Fields.GetValue("offsety_top", 0.0));

			int x = sidedef.OffsetX + (int)Math.Round(sidedef.Fields.GetValue("offsetx_top", 0.0));
			int y = (int)Tools.GetSidedefTopOffsetY(sidedef, sidedef.OffsetY + localY, scaleY, false);
			CheckAlignment(sidedef, x, y, scaleX, scaleY, VisualGeometryType.WALL_UPPER, sidedef.HighTexture);
		}

		private void CheckMiddleAlignment(Sidedef sidedef) 
		{
			if(!sidedef.MiddleRequired() || sidedef.LongMiddleTexture == MapSet.EmptyLongName) return;
			
			double scaleX = sidedef.Fields.GetValue("scalex_mid", 1.0);
			double scaleY = sidedef.Fields.GetValue("scaley_mid", 1.0);
			int localY = (int)Math.Round(sidedef.Fields.GetValue("offsety_mid", 0.0));

			int x = sidedef.OffsetX + (int)Math.Round(sidedef.Fields.GetValue("offsetx_mid", 0.0));
			int y = (int)Tools.GetSidedefMiddleOffsetY(sidedef, sidedef.OffsetY + localY, scaleY, false);
			CheckAlignment(sidedef, x, y, scaleX, scaleY, VisualGeometryType.WALL_MIDDLE, sidedef.MiddleTexture);
		}

		private void CheckBottomAlignment(Sidedef sidedef) 
		{
			if(!sidedef.LowRequired() || sidedef.LongLowTexture == MapSet.EmptyLongName) return;

			double scaleX = sidedef.Fields.GetValue("scalex_bottom", 1.0);
			double scaleY = sidedef.Fields.GetValue("scaley_bottom", 1.0);
			int localY = (int)Math.Round(sidedef.Fields.GetValue("offsety_bottom", 0.0));

			int x = sidedef.OffsetX + (int)Math.Round(sidedef.Fields.GetValue("offsetx_bottom", 0.0));
			int y = (int)Tools.GetSidedefBottomOffsetY(sidedef, sidedef.OffsetY + localY, scaleY, false);
			CheckAlignment(sidedef, x, y, scaleX, scaleY, VisualGeometryType.WALL_LOWER, sidedef.LowTexture);
		}

		#endregion

		#region ================== Methods

		private void CheckAlignment(Sidedef sidedef, int offsetx, int offsety, double linescalex, double linescaley, VisualGeometryType parttype, string texturename) 
		{
			ImageData texture = General.Map.Data.GetTextureImage(texturename);
			if(texture == null || !texture.IsImageLoaded || texture.Width <= 0 || texture.Height <= 0) return;
			if(double.IsNaN(linescalex) || double.IsInfinity(linescalex) || Math.Abs(linescalex) < 0.000001
				|| double.IsNaN(linescaley) || double.IsInfinity(linescaley) || Math.Abs(linescaley) < 0.000001) return;
			if(sidedef.Line == null || sidedef.Sector == null) return;
			Rectangle partsize = BuilderModesTools.GetSidedefPartSize(sidedef, parttype);

			double scalex = ((General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.x : 1.0f);
			double scaley = ((General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.y : 1.0f);
			if(!IsValidScale(scalex) || !IsValidScale(scaley)) return;

			// Move offsets to proper range
			offsetx %= texture.Width;
			if(offsetx < 0) offsetx += texture.Width;
			offsety %= texture.Height;
			if(offsety < 0) offsety += texture.Height;

			// Check if current line is aligned to other sides
			ICollection<Linedef> lines = (sidedef.IsFront ? sidedef.Line.Start.Linedefs : sidedef.Line.End.Linedefs);
			Vertex v = sidedef.IsFront ? sidedef.Line.Start : sidedef.Line.End;

			foreach(Linedef line in lines) 
			{
				if(line.Index == sidedef.Line.Index) continue;

				Sidedef target = null;
				if(line.Front != null && line.End == v) target = line.Front;
				else if(line.Back != null && line.Start == v) target = line.Back;

				// No target?
				if(target == null) continue;

				// Get expected texture offsets
				VisualGeometryType targetparttype;
				int alignedY = GetExpectedOffsetY(sidedef, target, texturename, texture.Height, scaley, linescaley, partsize, out targetparttype);
				if(targetparttype == VisualGeometryType.UNKNOWN) continue;

				// Already added?
				AlignmentPair pair = new AlignmentPair(sidedef, parttype, target, targetparttype);
				if(donesides.Contains(pair))
					continue;

				bool misaligned = false;
				// Not aligned if scale Y is not equal
				double targetscaley = GetSidedefValue(target, targetparttype, "scaley", 1.0);
				double targetscalex = GetSidedefValue(target, targetparttype, "scalex", 1.0);
				if(!IsValidScale(targetscalex) || !IsValidScale(targetscaley))
				{
					SubmitResult(new ResultTexturesMisaligned(sidedef, target, texturename));
					donesides.Add(pair);
					continue;
				}
				if(Math.Abs(targetscaley - linescaley) > 0.0001) misaligned = true;

				alignedY %= texture.Height;
				if(alignedY < 0) alignedY += texture.Height;

				int alignedX = (target.OffsetX + (int)GetSidedefValue(target, targetparttype, "offsetx", 0f) + (int)Math.Round(target.Line.Length / scalex * targetscalex)) % texture.Width;
				if(alignedX < 0) alignedX += texture.Width;

				// Submit result if target offsets don't match expected ones
				if(offsetx != alignedX || offsety != alignedY) misaligned = true;
				if(misaligned) SubmitResult(new ResultTexturesMisaligned(sidedef, target, texturename));

				// Add to collection
				donesides.Add(pair);
			}

			// Check if other sides are aligned to current side
			lines = (sidedef.IsFront ? sidedef.Line.End.Linedefs : sidedef.Line.Start.Linedefs);
			v = (sidedef.IsFront ? sidedef.Line.End : sidedef.Line.Start);

			foreach(Linedef line in lines) 
			{
				if(line.Index == sidedef.Line.Index) continue;

				Sidedef target = null;
				if(line.Front != null && line.Start == v) target = line.Front;
				else if(line.Back != null && line.End == v) target = line.Back;

				// No target or laready processed?
				if(target == null) continue;

				// Get expected texture offsets
				VisualGeometryType targetparttype;
				int alignedY = GetExpectedOffsetY(sidedef, target, texturename, texture.Height, scaley, linescaley, partsize, out targetparttype);
				if(targetparttype == VisualGeometryType.UNKNOWN) continue;

				// Already added?
				AlignmentPair pair = new AlignmentPair(sidedef, parttype, target, targetparttype);
				if(donesides.Contains(pair))
					continue;

				bool misaligned = false;
				// Not aligned if scale Y is not equal
				double targetscaley = GetSidedefValue(target, targetparttype, "scaley", 1.0);
				double targetscalex = GetSidedefValue(target, targetparttype, "scalex", 1.0);
				if(!IsValidScale(targetscalex) || !IsValidScale(targetscaley))
				{
					SubmitResult(new ResultTexturesMisaligned(sidedef, target, texturename));
					donesides.Add(pair);
					continue;
				}
				if(Math.Abs(targetscaley - linescaley) > 0.0001) misaligned = true;

				alignedY %= texture.Height;
				if(alignedY < 0) alignedY += texture.Height;

				int alignedX = (target.OffsetX + (int)GetSidedefValue(target, targetparttype, "offsetx", 0.0) - (int)Math.Round(sidedef.Line.Length / scalex * linescalex)) % texture.Width;
				if(alignedX < 0) alignedX += texture.Width;

				// Submit result if target offsets don't match expected ones
				if(offsetx != alignedX || offsety != alignedY) misaligned = true;
				if(misaligned) SubmitResult(new ResultTexturesMisaligned(sidedef, target, texturename));

				// Add to collection
				donesides.Add(pair);
			}
		}

		private static double GetSidedefValue(Sidedef target, VisualGeometryType targetparttype, string key, double defaultvalue) 
		{
			switch(targetparttype)
			{
				case VisualGeometryType.WALL_UPPER:
					return Math.Round(UniFields.GetFloat(target.Fields, key + "_top", defaultvalue), 3);

				case VisualGeometryType.WALL_MIDDLE:
					return Math.Round(UniFields.GetFloat(target.Fields, key + "_mid", defaultvalue), 3);

				case VisualGeometryType.WALL_LOWER:
					return Math.Round(UniFields.GetFloat(target.Fields, key + "_bottom", defaultvalue), 3);
			}

			return 0;
		}

		private static bool IsValidScale(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) >= 0.000001;
		}

		private static int GetExpectedOffsetY(Sidedef source, Sidedef target, string texturename, int textureheight, double texturescaley, double linescaley, Rectangle partsize, out VisualGeometryType matchingparttype) 
		{
			if(source == null || target == null || source.Sector == null || target.Sector == null
				|| textureheight <= 0 || Math.Abs(texturescaley) < 0.000001)
			{
				matchingparttype = VisualGeometryType.UNKNOWN;
				return int.MinValue;
			}
			if(target.MiddleTexture == texturename
					&& partsize.IntersectsWith(BuilderModesTools.GetSidedefPartSize(target, VisualGeometryType.WALL_MIDDLE)))
			{
				matchingparttype = VisualGeometryType.WALL_MIDDLE;
				int partheight = (int)Math.Round(((target.Sector.CeilHeight - source.Sector.CeilHeight) / texturescaley) * linescaley);
				return ((int)Tools.GetSidedefMiddleOffsetY(target, target.OffsetY + GetSidedefValue(target, matchingparttype, "offsety", 0f), linescaley, false) + partheight) % textureheight;
			}

			// Only check upper and lower textures if the sidedef as an other side. See https://github.com/jewalky/UltimateDoomBuilder/issues/533
			if (target.Other != null && target.HighTexture == texturename
					&& partsize.IntersectsWith(BuilderModesTools.GetSidedefPartSize(target, VisualGeometryType.WALL_UPPER)))
			{
				matchingparttype = VisualGeometryType.WALL_UPPER;
				int partheight = (int) Math.Round(((target.Sector.CeilHeight - source.Sector.CeilHeight) / texturescaley) * linescaley);
				return ((int)Tools.GetSidedefTopOffsetY(target, target.OffsetY + GetSidedefValue(target, matchingparttype, "offsety", 0f), linescaley, false) + partheight) % textureheight;
			}

			if (target.Other != null && target.LowTexture == texturename
				  && partsize.IntersectsWith(BuilderModesTools.GetSidedefPartSize(target, VisualGeometryType.WALL_LOWER)))
			{
				matchingparttype = VisualGeometryType.WALL_LOWER;
				int partheight = (int)Math.Round(((target.Sector.CeilHeight - source.Sector.CeilHeight) / texturescaley) * linescaley);
				return ((int)Tools.GetSidedefBottomOffsetY(target, target.OffsetY + GetSidedefValue(target, matchingparttype, "offsety", 0f), linescaley, false) + partheight) % textureheight;
			}

			matchingparttype = VisualGeometryType.UNKNOWN;
			return int.MinValue;
		}

		#endregion
	}
}
