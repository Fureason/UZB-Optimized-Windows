
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
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Geometry;
using System.Drawing;

#endregion

namespace CodeImp.DoomBuilder.VisualModes
{
    public sealed class VisualBlockMap
    {
		// This returns all blocks along the given line
		public List<VisualBlockEntry> GetLineBlocks(Vector2D v1, Vector2D v2)
        {
			var result = new List<VisualBlockEntry>();
			GetLineBlocks(v1, v2, result);
			return result;
		}

		// This fills a caller-owned collection with all blocks along the given line.
		// Visual mode calls this several times per second, so reusing the collection
		// avoids a steady stream of short-lived allocations on large maps.
		public void GetLineBlocks(Vector2D v1, Vector2D v2, List<VisualBlockEntry> result)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			result.Clear();

			// A rectangular query becomes extremely broad for diagonal picking rays and
			// can cover most of a large map. Traverse only nodes touched by the actual
			// segment instead. Entries stored at parent nodes are still included, so
			// long lines and large things remain pickable.
			root.GetBlocks(new Line2D(v1, v2), ref result);
		}

        public List<VisualBlockEntry> GetBlocks(RectangleF box)
        {
            var result = new List<VisualBlockEntry>();
            root.GetBlocks(ToRectangle(box), ref result);
            return result;
        }

        public List<VisualBlockEntry> GetBlocks(Vector2D pos)
        {
			var result = new List<VisualBlockEntry>();
			GetBlocks(pos, result);
			return result;
        }

		public void GetBlocks(Vector2D pos, List<VisualBlockEntry> result)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			result.Clear();
			root.GetBlocks(new Point((int)Math.Floor(pos.x), (int)Math.Floor(pos.y)), ref result);
		}

        // This returns a range of blocks in a frustum
        public List<VisualBlockEntry> GetFrustumRange(ProjectedFrustum2D frustum2D)
        {
            var result = new List<VisualBlockEntry>();
			GetFrustumRange(frustum2D, result);
            return result;
        }

		public void GetFrustumRange(ProjectedFrustum2D frustum2D, List<VisualBlockEntry> result)
		{
			GetFrustumRange(frustum2D, new Vector2D(), result);
		}

		// Fill a caller-owned collection in near-to-far order. Visual mode creates
		// sector resources lazily, so visiting the camera quadrant first makes the
		// surface in front of the user available before distant map geometry.
		public void GetFrustumRange(ProjectedFrustum2D frustum2D, Vector2D origin, List<VisualBlockEntry> result)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			result.Clear();

			for (int i = 0; i < reusableFrustum.planes.Length; i++)
				reusableFrustum.planes[i] = new Plane(frustum2D.Lines[i]);

			root.GetBlocks(reusableFrustum, origin, ref result);
		}

        public Sector GetSectorAt(Vector2D pos)
        {
			return GetSectorAt(pos, new List<VisualBlockEntry>(), new List<Sector>(1));
		}

		// Scratch collections are supplied by VisualMode so this per-frame query
		// does not allocate. They are cleared before use and never retained.
		public Sector GetSectorAt(Vector2D pos, List<VisualBlockEntry> blocks, List<Sector> sectors)
		{
			return GetSectorAt(pos, blocks, sectors, null);
		}

		// Reuse the caller's linedef set when self-referencing sectors overlap at the
		// camera point. This case is common in advanced maps and used to allocate a
		// new set on every movement update.
		public Sector GetSectorAt(Vector2D pos, List<VisualBlockEntry> blocks, List<Sector> sectors,
			HashSet<Linedef> linedefs)
		{
			if (blocks == null) throw new ArgumentNullException(nameof(blocks));
			if (sectors == null) throw new ArgumentNullException(nameof(sectors));
			sectors.Clear();

			GetBlocks(pos, blocks);
			foreach (VisualBlockEntry e in blocks)
                foreach (Sector s in e.Sectors)
                    if (s.Intersect(pos))
						sectors.Add(s);

			if (sectors.Count == 0)
			{
				return null;
			}
			else if (sectors.Count == 1)
			{
				return sectors[0];
			}
			else
			{
				// Having multiple intersections indicates that there are self-referencing sectors in this spot.
				// In this case we have to check which side of the nearest linedef pos is on, and then use that sector
				if(linedefs == null)
					linedefs = new HashSet<Linedef>(sectors[0].Sidedefs.Count * sectors.Count);
				else
					linedefs.Clear();

				foreach (Sector s in sectors)
					foreach (Sidedef sd in s.Sidedefs)
						linedefs.Add(sd.Line);

				Linedef nearest = MapSet.NearestLinedef(linedefs, pos);
				double d = nearest.SideOfLine(pos);

				if (d <= 0.0 && nearest.Front != null)
					return nearest.Front.Sector;
				else if (nearest.Back != null)
					return nearest.Back.Sector;
			}

            return null;
        }

        public void Clear()
        {
            root = new Node(new Rectangle(General.Map.Config.LeftBoundary, General.Map.Config.BottomBoundary, General.Map.Config.RightBoundary - General.Map.Config.LeftBoundary, General.Map.Config.TopBoundary - General.Map.Config.BottomBoundary));
        }

		public void AddSectorsSet(ICollection<Sector> sectors)
		{
            foreach (Sector s in sectors) AddSector(s);
		}

        public void AddLinedefsSet(ICollection<Linedef> lines)
        {
            foreach (Linedef line in lines) AddLinedef(line);
        }

        public void AddThingsSet(ICollection<Thing> things)
        {
            foreach (Thing t in things) AddThing(t);
        }

        public void AddSector(Sector sector)
        {
            root.GetEntry(ToRectangle(sector.BBox)).Sectors.Add(sector);
        }

        public void AddLinedef(Linedef line)
        {
            int x0 = (int)Math.Floor(Math.Min(line.Start.Position.x, line.End.Position.x));
            int y0 = (int)Math.Floor(Math.Min(line.Start.Position.y, line.End.Position.y));
            int x1 = (int)Math.Floor(Math.Max(line.Start.Position.x, line.End.Position.x)) + 1;
            int y1 = (int)Math.Floor(Math.Max(line.Start.Position.y, line.End.Position.y)) + 1;
            root.GetEntry(new Rectangle(x0, y0, x1 - x0, y1 - y0)).Lines.Add(line);
        }

        public void AddThing(Thing thing)
        {
            int x0 = (int)Math.Floor(thing.Position.x - thing.Size);
            int x1 = (int)Math.Floor(thing.Position.x + thing.Size) + 1;
            int y0 = (int)Math.Floor(thing.Position.y - thing.Size);
            int y1 = (int)Math.Floor(thing.Position.y + thing.Size) + 1;
            root.GetEntry(new Rectangle(x0, y0, x1 - x0, y1 - y0)).Things.Add(thing);
        }

        internal void Dispose()
		{
            Clear();
		}

        static Rectangle ToRectangle(RectangleF bbox)
        {
            int x0 = (int)Math.Floor(bbox.Left);
            int y0 = (int)Math.Floor(bbox.Top);
            int x1 = (int)Math.Floor(bbox.Right) + 1;
            int y1 = (int)Math.Floor(bbox.Bottom) + 1;
            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        const int MaxLevels = 8;

        Node root = new Node(new Rectangle(General.Map.Config.LeftBoundary, General.Map.Config.BottomBoundary, General.Map.Config.RightBoundary - General.Map.Config.LeftBoundary, General.Map.Config.TopBoundary - General.Map.Config.BottomBoundary));

		struct Plane
		{
			public Plane(Line2D line)
            {
                Vector2D dir = line.v2 - line.v1;
				A = -dir.y;
				B = dir.x;
				D = -(line.v1.x * A + line.v1.y * B);
				AbsA = Math.Abs(A);
				AbsB = Math.Abs(B);
			}

			public double A, B, D, AbsA, AbsB;
        }

        class Frustum
        {
			public readonly Plane[] planes = new Plane[4];
        }

		private readonly Frustum reusableFrustum = new Frustum();

        class Node
        {
            enum Visibility { Inside, Intersecting, Outside };

            public Node(Rectangle bbox)
            {
                this.bbox = bbox;
                extents = new Vector2D(bbox.Width * 0.5f, bbox.Height * 0.5f);
                center = new Vector2D(bbox.X + extents.x, bbox.Y + extents.y);
            }

            public void GetBlocks(Frustum frustum, Vector2D origin, ref List<VisualBlockEntry> list)
            {
                Visibility vis = TestVisibility(frustum);
                if (vis == Visibility.Inside)
                {
                    GetAllBlocks(origin, ref list);
                }
                else if (vis == Visibility.Intersecting)
                {
                    if (visualBlock != null)
                        list.Add(visualBlock);

                    if (topLeft != null)
                    {
						GetFrustumChildrenNearFirst(frustum, origin, ref list);
                    }
                }
            }

            private void GetFrustumChildrenNearFirst(Frustum frustum, Vector2D origin, ref List<VisualBlockEntry> list)
			{
				bool left = origin.x < center.x;
				bool top = origin.y < center.y;

				if(top)
				{
					if(left)
					{
						topLeft.GetBlocks(frustum, origin, ref list);
						topRight.GetBlocks(frustum, origin, ref list);
						bottomLeft.GetBlocks(frustum, origin, ref list);
						bottomRight.GetBlocks(frustum, origin, ref list);
					}
					else
					{
						topRight.GetBlocks(frustum, origin, ref list);
						topLeft.GetBlocks(frustum, origin, ref list);
						bottomRight.GetBlocks(frustum, origin, ref list);
						bottomLeft.GetBlocks(frustum, origin, ref list);
					}
				}
				else if(left)
				{
					bottomLeft.GetBlocks(frustum, origin, ref list);
					topLeft.GetBlocks(frustum, origin, ref list);
					bottomRight.GetBlocks(frustum, origin, ref list);
					topRight.GetBlocks(frustum, origin, ref list);
				}
				else
				{
					bottomRight.GetBlocks(frustum, origin, ref list);
					topRight.GetBlocks(frustum, origin, ref list);
					bottomLeft.GetBlocks(frustum, origin, ref list);
					topLeft.GetBlocks(frustum, origin, ref list);
				}
			}

            void GetAllBlocks(Vector2D origin, ref List<VisualBlockEntry> list)
            {
                if (visualBlock != null)
                    list.Add(visualBlock);

                if (topLeft != null)
                {
					GetAllChildrenNearFirst(origin, ref list);
                }
            }

			private void GetAllChildrenNearFirst(Vector2D origin, ref List<VisualBlockEntry> list)
			{
				bool left = origin.x < center.x;
				bool top = origin.y < center.y;

				if(top)
				{
					if(left)
					{
						topLeft.GetAllBlocks(origin, ref list);
						topRight.GetAllBlocks(origin, ref list);
						bottomLeft.GetAllBlocks(origin, ref list);
						bottomRight.GetAllBlocks(origin, ref list);
					}
					else
					{
						topRight.GetAllBlocks(origin, ref list);
						topLeft.GetAllBlocks(origin, ref list);
						bottomRight.GetAllBlocks(origin, ref list);
						bottomLeft.GetAllBlocks(origin, ref list);
					}
				}
				else if(left)
				{
					bottomLeft.GetAllBlocks(origin, ref list);
					topLeft.GetAllBlocks(origin, ref list);
					bottomRight.GetAllBlocks(origin, ref list);
					topRight.GetAllBlocks(origin, ref list);
				}
				else
				{
					bottomRight.GetAllBlocks(origin, ref list);
					topRight.GetAllBlocks(origin, ref list);
					bottomLeft.GetAllBlocks(origin, ref list);
					topLeft.GetAllBlocks(origin, ref list);
				}
			}

            Visibility TestVisibility(Frustum frustum)
            {
                Visibility result = Visibility.Inside;
                for (int i = 0; i < 4; i++)
                {
                    Visibility vis = TestFrustumLineVisibility(frustum.planes[i]);
                    if (vis == Visibility.Outside)
                        return Visibility.Outside;
                    else if (vis == Visibility.Intersecting)
                        result = Visibility.Intersecting;
                }
                return result;
            }

			Visibility TestFrustumLineVisibility(Plane plane)
			{
				double e = extents.x * plane.AbsA + extents.y * plane.AbsB;
				double s = center.x * plane.A + center.y * plane.B + plane.D;
                if (s - e > 0.0)
                    return Visibility.Inside;
                else if (s + e < 0)
                    return Visibility.Outside;
                else
                    return Visibility.Intersecting;
            }

			public void GetBlocks(Point pos, ref List<VisualBlockEntry> list)
			{
				if (visualBlock != null)
					list.Add(visualBlock);

				if (topLeft != null)
				{
					// Child rectangles do not overlap. Select the matching quadrant once
					// instead of performing four rectangle tests at every tree level.
					Node child;
					if(pos.Y < center.y)
						child = (pos.X < center.x ? topLeft : topRight);
					else
						child = (pos.X < center.x ? bottomLeft : bottomRight);

					if(child.bbox.Contains(pos)) child.GetBlocks(pos, ref list);
				}
			}

			public void GetBlocks(Rectangle box, ref List<VisualBlockEntry> list)
			{
                if (visualBlock != null)
                    list.Add(visualBlock);

                if (topLeft != null)
                {
                    if (topLeft.bbox.IntersectsWith(box)) topLeft.GetBlocks(box, ref list);
                    if (topRight.bbox.IntersectsWith(box)) topRight.GetBlocks(box, ref list);
                    if (bottomLeft.bbox.IntersectsWith(box)) bottomLeft.GetBlocks(box, ref list);
                    if (bottomRight.bbox.IntersectsWith(box)) bottomRight.GetBlocks(box, ref list);
				}
			}

			public void GetBlocks(Line2D line, ref List<VisualBlockEntry> list)
			{
				if(!LineIntersectsRectangle(line, bbox)) return;

				if(visualBlock != null)
					list.Add(visualBlock);

				if(topLeft != null)
				{
					topLeft.GetBlocks(line, ref list);
					topRight.GetBlocks(line, ref list);
					bottomLeft.GetBlocks(line, ref list);
					bottomRight.GetBlocks(line, ref list);
				}
			}

			// Liang-Barsky segment clipping. This avoids allocating RectangleF or
			// Line2D helpers in the continuously-running visual-mode picker.
			private static bool LineIntersectsRectangle(Line2D line, Rectangle box)
			{
				double dx = line.v2.x - line.v1.x;
				double dy = line.v2.y - line.v1.y;
				double t0 = 0.0;
				double t1 = 1.0;

				return Clip(-dx, line.v1.x - box.Left, ref t0, ref t1)
					&& Clip(dx, box.Right - line.v1.x, ref t0, ref t1)
					&& Clip(-dy, line.v1.y - box.Top, ref t0, ref t1)
					&& Clip(dy, box.Bottom - line.v1.y, ref t0, ref t1);
			}

			private static bool Clip(double p, double q, ref double t0, ref double t1)
			{
				if(p == 0.0) return q >= 0.0;

				double r = q / p;
				if(p < 0.0)
				{
					if(r > t1) return false;
					if(r > t0) t0 = r;
				}
				else
				{
					if(r < t0) return false;
					if(r < t1) t1 = r;
				}

				return true;
			}

            public VisualBlockEntry GetEntry(Rectangle box, int level = 0)
            {
                if (level == MaxLevels)
                {
                    if (visualBlock == null)
                        visualBlock = new VisualBlockEntry();
                    return visualBlock;
                }

                if (topLeft == null)
                    CreateChildren();

                if (topLeft.bbox.Contains(box)) return topLeft.GetEntry(box, level + 1);
                if (topRight.bbox.Contains(box)) return topRight.GetEntry(box, level + 1);
                if (bottomLeft.bbox.Contains(box)) return bottomLeft.GetEntry(box, level + 1);
                if (bottomRight.bbox.Contains(box)) return bottomRight.GetEntry(box, level + 1);

                if (visualBlock == null)
                    visualBlock = new VisualBlockEntry();
                return visualBlock;
            }

            void CreateChildren()
            {
                int x0 = bbox.X;
                int x1 = bbox.X + bbox.Width / 2;
                int x2 = bbox.X + bbox.Width;
                int y0 = bbox.Y;
                int y1 = bbox.Y + bbox.Height / 2;
                int y2 = bbox.Y + bbox.Height;
                topLeft = new Node(new Rectangle(x0, y0, x1 - x0, y1 - y0));
                topRight = new Node(new Rectangle(x1, y0, x2 - x1, y1 - y0));
                bottomLeft = new Node(new Rectangle(x0, y1, x1 - x0, y2 - y1));
                bottomRight = new Node(new Rectangle(x1, y1, x2 - x1, y2 - y1));
            }

            Rectangle bbox;
            Vector2D extents;
            Vector2D center;

            Node topLeft;
            Node topRight;
            Node bottomLeft;
            Node bottomRight;
            VisualBlockEntry visualBlock;
        }
    }
}
