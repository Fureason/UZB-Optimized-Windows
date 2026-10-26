
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
using System.Threading;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	[ErrorChecker("Check overlapping lines", true, 500)]
	public class CheckOverlappingLines : ErrorChecker
	{
		#region ================== Constants

		private const int PROGRESS_STEP = 100;

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public CheckOverlappingLines()
		{
			// Total progress is done when all lines are checked
			SetTotalProgress(General.Map.Map.Linedefs.Count / PROGRESS_STEP);
		}
		
		#endregion
		
		#region ================== Methods
		
		// This runs the check
		public override void Run()
		{
			HashSet<Linedef> donelines = new HashSet<Linedef>();
			BlockMap<BlockEntry> blockmap = BuilderPlug.Me.ErrorCheckForm.BlockMap;
			int progress = 0;
			int stepprogress = 0;

			// Go for all the liendefs
			foreach(Linedef l in General.Map.Map.Linedefs)
			{
				// Check if not already done
				if(!donelines.Contains(l))
				{
					// And go for all the linedefs that could overlap
					List<BlockEntry> blocks = blockmap.GetLineBlocks(l.Start.Position, l.End.Position);
					HashSet<Linedef> doneblocklines = new HashSet<Linedef>();
					foreach(BlockEntry b in blocks)
					{
						foreach(Linedef d in b.Lines)
						{
							// Not the same line and not already checked
							if(!object.ReferenceEquals(l, d) && !doneblocklines.Contains(d))
							{
								double lu, du;

								// Temporary lines
								Line2D tl = l.Line;
								Line2D td = d.Line;

								// If vertices are off-grid and far from the map's origin the calculation of the intersection can go wrong because of rounding errors.
								// So if any vertex is off-grid we'll to the calculations with lines that are closer to the origin. This is pretty ugly :(
								// See https://github.com/jewalky/UltimateDoomBuilder/issues/713
								if (General.Map.FormatInterface.VertexDecimals > 0 &&
									l.Line.v1.x % 1 != 0.0 && l.Line.v1.y % 1 != 0.0 && l.Line.v2.x % 1 != 0.0 && l.Line.v2.y % 1 != 0.0 &&
									d.Line.v1.x % 1 != 0.0 && d.Line.v1.y % 1 != 0.0 && d.Line.v2.x % 1 != 0.0 && d.Line.v2.y % 1 != 0.0)
								{
									// Create the offset we want to move the lines by. It is getting the most extreme values of the vertices
									Vector2D offset = new Vector2D(
										(int)ClosestToZero(l.Line.v1.x, l.Line.v2.x, d.Line.v1.x, d.Line.v2.x),
										(int)ClosestToZero(l.Line.v1.y, l.Line.v2.y, d.Line.v1.y, d.Line.v2.y)
									);

									// Create the two lines to check. this takes the original values, applies the offset, then rounds them to the map format's precision
									tl = new Line2D(
										new Vector2D(Math.Round(l.Line.v1.x - offset.x, General.Map.FormatInterface.VertexDecimals), Math.Round(l.Line.v1.y - offset.y, General.Map.FormatInterface.VertexDecimals)),
										new Vector2D(Math.Round(l.Line.v2.x - offset.x, General.Map.FormatInterface.VertexDecimals), Math.Round(l.Line.v2.y - offset.y, General.Map.FormatInterface.VertexDecimals))
									);

									td = new Line2D(
										new Vector2D(Math.Round(d.Line.v1.x - offset.x, General.Map.FormatInterface.VertexDecimals), Math.Round(d.Line.v1.y - offset.y, General.Map.FormatInterface.VertexDecimals)),
										new Vector2D(Math.Round(d.Line.v2.x - offset.x, General.Map.FormatInterface.VertexDecimals), Math.Round(d.Line.v2.y - offset.y, General.Map.FormatInterface.VertexDecimals))
									);
								}
								
								//mxd. This can also happen. I suppose. Some people manage to do this. I dunno how, but they do...
								if((l.Start.Position == d.Start.Position && l.End.Position == d.End.Position)
									|| (l.Start.Position == d.End.Position && l.End.Position == d.Start.Position)) 
								{
									SubmitResult(new ResultLineOverlapping(l, d));
									donelines.Add(d);
								} 
								else if(tl.GetIntersection(td, out du, out lu)) 
								{
									// Check if the lines touch. Note that I don't include 0.0 and 1.0 here because
									// the lines may be touching at the ends when sharing the same vertex.
									if(General.Map.FormatInterface.VertexDecimals > 0) //mxd
									{
										lu = Math.Round(lu, General.Map.FormatInterface.VertexDecimals);
										du = Math.Round(du, General.Map.FormatInterface.VertexDecimals);
									}

									if ((lu > 0.0) && (lu < 1.0) && (du > 0.0) && (du < 1.0))
									{
										// Check if not the same sector on all sides
										Sector samesector = null;
										if (l.Front != null) samesector = l.Front.Sector;
										else if (l.Back != null) samesector = l.Back.Sector;
										else if (d.Front != null) samesector = d.Front.Sector;
										else if (d.Back != null) samesector = d.Back.Sector;

										if ((l.Front == null) || (l.Front.Sector != samesector)) samesector = null;
										else if ((l.Back == null) || (l.Back.Sector != samesector)) samesector = null;
										else if ((d.Front == null) || (d.Front.Sector != samesector)) samesector = null;
										else if ((d.Back == null) || (d.Back.Sector != samesector)) samesector = null;

										if (samesector == null)
										{
											SubmitResult(new ResultLineOverlapping(l, d));
											donelines.Add(d);
										}
									}
								}
								
								// Checked
								doneblocklines.Add(d);
							}
						}
					}
				}
				
				// Handle thread interruption
				if((progress & 63) == 0 && YieldForCancellation()) return;

				// We are making progress!
				if((++progress / PROGRESS_STEP) > stepprogress)
				{
					stepprogress = (progress / PROGRESS_STEP);
					AddProgress(1);
				}
			}
		}

		private static double ClosestToZero(double a, double b, double c, double d)
		{
			double result = a;
			if(Math.Abs(b) < Math.Abs(result)) result = b;
			if(Math.Abs(c) < Math.Abs(result)) result = c;
			if(Math.Abs(d) < Math.Abs(result)) result = d;
			return result;
		}
		
		#endregion
	}
}
