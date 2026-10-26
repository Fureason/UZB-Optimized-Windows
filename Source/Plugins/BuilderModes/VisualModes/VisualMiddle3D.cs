
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
using System.Drawing;
using System.Globalization;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.Data;
using System.Windows.Forms;


#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal class VisualMiddle3D : BaseVisualGeometrySidedef
	{
		#region ================== Constants

		#endregion
		
		#region ================== Variables

		protected Effect3DFloor extrafloor;

		#endregion

		#region ================== Properties

		public Effect3DFloor ExtraFloor { get { return extrafloor; } }
		public override MapElement3D AsMapElement3D
		{
			get
			{
				Linedef controlline = GetControlLinedef();
				return (controlline != null && Sidedef != null && !Sidedef.IsDisposed)
					? new ThreeDFloorSide3D(controlline, Sidedef) : null;
			}
		}

		#endregion

		#region ================== Constructor / Setup

		// Constructor
		public VisualMiddle3D(BaseVisualMode mode, VisualSector vs, Sidedef s) : base(mode, vs, s)
		{
			//mxd
			geometrytype = VisualGeometryType.WALL_MIDDLE_3D;
			partname = "mid";
			
			// We have no destructor
			GC.SuppressFinalize(this);
		}
		
		// This builds the geometry. Returns false when no geometry created.
		public override bool Setup() { return this.Setup(this.extrafloor); }
		public bool Setup(Effect3DFloor extrafloor)
		{
			this.extrafloor = extrafloor;
			Sidedef sourceside;
			// "Don't render sides" is an in-game presentation flag. Keep those sides
			// visible in the editor so the FOF can still be selected, aligned and checked.
			if(extrafloor == null)
			{
				base.SetVertices(null);
				return false;
			}

			// An undo can replace the control linedef before this retained visual sector
			// is rebuilt. Never build or interact with geometry backed by that stale FOF.
			if(!TryGetControlSide(out sourceside) || Sidedef == null || Sidedef.IsDisposed
				|| Sidedef.Line == null || Sidedef.Line.IsDisposed || Sidedef.Other == null)
			{
				base.SetVertices(null);
				return false;
			}

			Vector2D vl, vr;

			bool useuppertexture = (sourceside.Line.Args[2] & (int)Effect3DFloor.Flags.UseUpperTexture) == (int)Effect3DFloor.Flags.UseUpperTexture;
			bool uselowertexture = (sourceside.Line.Args[2] & (int)Effect3DFloor.Flags.UseLowerTexture) == (int)Effect3DFloor.Flags.UseLowerTexture;

			//mxd. lightfog flag support
			int lightvalue;
			bool lightabsolute;
			GetLightValue(out lightvalue, out lightabsolute);

			Vector2D tscale;
			Vector2D toffset2 = new Vector2D(0.0, 0.0);

			// If the upper or lower textures are used we have to take the scale of those, not of the source
			// Not in SRB2, though
			//if (useuppertexture)
			//	tscale = new Vector2D(Sidedef.Fields.GetValue("scalex_top", 1.0), Sidedef.Fields.GetValue("scaley_top", 1.0));
			//else if (uselowertexture)
			//	tscale = new Vector2D(Sidedef.Fields.GetValue("scalex_bottom", 1.0), Sidedef.Fields.GetValue("scaley_bottom", 1.0));
			//else
			{
				tscale = new Vector2D(sourceside.Fields.GetValue("scalex_mid", 1.0), sourceside.Fields.GetValue("scaley_mid", 1.0));

				// The offset of the source side is only taken into account when not using the upper or lower textures
				toffset2 = new Vector2D(sourceside.Fields.GetValue("offsetx_mid", 0.0), sourceside.Fields.GetValue("offsety_mid", 0.0));
			}

			Vector2D tscaleAbs = new Vector2D(Math.Abs(tscale.x), Math.Abs(tscale.y));
			Vector2D toffset1 = new Vector2D(Sidedef.Fields.GetValue("offsetx_mid", 0.0), 0.0); // SRB2 only uses middle X offset
											 //Sidedef.Fields.GetValue("offsety_mid", 0.0));
		
			// Left and right vertices for this sidedef
			if(Sidedef.IsFront)
			{
				vl = new Vector2D(Sidedef.Line.Start.Position.x, Sidedef.Line.Start.Position.y);
				vr = new Vector2D(Sidedef.Line.End.Position.x, Sidedef.Line.End.Position.y);
			}
			else
			{
				vl = new Vector2D(Sidedef.Line.End.Position.x, Sidedef.Line.End.Position.y);
				vr = new Vector2D(Sidedef.Line.Start.Position.x, Sidedef.Line.Start.Position.y);
			}
			
			// Load sector data
			SectorData sd = mode.GetSectorData(Sidedef.Sector);
			
			//mxd. which texture we must use?
			long texturelong = 0;
			if(useuppertexture) 
			{
				if(Sidedef.LongHighTexture != MapSet.EmptyLongName)
					texturelong = Sidedef.LongHighTexture;
			} 
			else if(uselowertexture) 
			{
				if(Sidedef.LongLowTexture != MapSet.EmptyLongName)
					texturelong = Sidedef.LongLowTexture;
			} 
			else if(sourceside.LongMiddleTexture != MapSet.EmptyLongName) 
			{
				texturelong = sourceside.LongMiddleTexture;
			}

			// Texture given?
			if(texturelong != 0)
			{
				// Load texture
				base.Texture = General.Map.Data.GetTextureImage(texturelong);
				if(base.Texture == null || base.Texture is UnknownImage)
				{
					base.Texture = General.Map.Data.UnknownTexture3D;
					setuponloadedtexture = texturelong;
				}
				else if(!base.Texture.IsImageLoaded)
				{
					setuponloadedtexture = texturelong;
				}
			}
			else
			{
				// Use missing texture
				base.Texture = General.Map.Data.MissingTexture3D;
				setuponloadedtexture = 0;
			}

			// Get texture scaled size. Round up, because that's apparently what GZDoom does
			Vector2D tsz = new Vector2D(base.Texture.ScaledWidth / tscale.x, base.Texture.ScaledHeight / tscale.y);
			
			// Get texture offsets
			//Vector2D tof = new Vector2D(Sidedef.OffsetX, Sidedef.OffsetY) + new Vector2D(sourceside.OffsetX, sourceside.OffsetY);
			Vector2D tof = new Vector2D(Sidedef.OffsetX, 0.0f) + new Vector2D(0.0f, sourceside.OffsetY);

			tof = tof + toffset1 + toffset2;

			// biwa. Also take the ForceWorldPanning MAPINFO entry into account
			if (General.Map.Config.ScaledTextureOffsets && (!base.Texture.WorldPanning && !General.Map.Data.MapInfo.ForceWorldPanning))
			{
				tof = tof / tscaleAbs;
				tof = tof * base.Texture.Scale;

				if (base.Texture is HiResImage) tof *= tscaleAbs;

				tof = new Vector2D(Math.Ceiling(tof.x), Math.Ceiling(tof.y));
			}

			// For Vavoom type 3D floors the ceiling is lower than floor and they are reversed.
			// We choose here.
			double sourcetopheight = extrafloor.VavoomType ? sourceside.Sector.FloorHeight : sourceside.Sector.CeilHeight;
			double sourcebottomheight = extrafloor.VavoomType ? sourceside.Sector.CeilHeight : sourceside.Sector.FloorHeight;

			// Determine texture coordinates plane as they would be in normal circumstances.
			// We can then use this plane to find any texture coordinate we need.
			// The logic here is the same as in the original VisualMiddleSingle (except that
			// the values are stored in a TexturePlane)
			// NOTE: I use a small bias for the floor height, because if the difference in
			// height is 0 then the TexturePlane doesn't work!
			Vector3D vlt, vlb, vrt, vrb;
			Vector2D tlt, tlb, trt, trb;
			bool lowerunpegged = sourceside.Line.IsFlagSet(General.Map.Config.LowerUnpeggedFlag);
			bool slopeskew = sourceside.Line.IsFlagSet(General.Map.Config.SlopeSkewFlag);

			double topheight = slopeskew ? extrafloor.Ceiling.plane.GetZ(vl) : sourcetopheight;
			double bottomheight = slopeskew ? extrafloor.Floor.plane.GetZ(vl) : sourcebottomheight;
			double topheight2 = slopeskew ? extrafloor.Ceiling.plane.GetZ(vr) : sourcetopheight;
			double bottomheight2 = slopeskew ? extrafloor.Floor.plane.GetZ(vr) : sourcebottomheight;
			//float floorbias = (topheight == bottomheight) ? 1.0f : 0.0f;
			//float floorbias2 = (topheight2 == bottomheight2) ? 1.0f : 0.0f;

			tlt.x = tlb.x = 0;
			trt.x = trb.x = Sidedef.Line.Length;
			// For SRB2, invert Lower Unpegged behavior for non-skewed 3D floors
			tlt.y = !(lowerunpegged ^ slopeskew) ? 0 : -(topheight - bottomheight);
			trt.y = !(lowerunpegged ^ slopeskew) ? 0 : -(topheight2 - bottomheight2);
			tlb.y = !(!lowerunpegged ^ slopeskew) ? 0 : (topheight - bottomheight);
			trb.y = !(!lowerunpegged ^ slopeskew) ? 0 : (topheight2 - bottomheight2);

			// Apply texture offset
			tlt += tof;
			tlb += tof;
			trb += tof;
			trt += tof;

			// Transform pixel coordinates to texture coordinates
			tlt /= tsz;
			tlb /= tsz;
			trb /= tsz;
			trt /= tsz;

			// Geometry coordinates
			vlt = new Vector3D(vl.x, vl.y, topheight);
			vlb = new Vector3D(vl.x, vl.y, bottomheight);
			vrb = new Vector3D(vr.x, vr.y, bottomheight2);
			vrt = new Vector3D(vr.x, vr.y, topheight2);

			TexturePlane tp = new TexturePlane();
			tp.tlt = lowerunpegged ? tlb : tlt;
			tp.trb = trb;
			tp.trt = trt;
			tp.vlt = lowerunpegged ? vlb : vlt;
			tp.vrb = vrb;
			tp.vrt = vrt;

			//mxd. Get ceiling and floor heights. Use our and neighbour sector's data
			SectorData sdo = mode.GetSectorData(Sidedef.Other.Sector);

			double flo = sdo.Floor.plane.GetZ(vl);
			double fro = sdo.Floor.plane.GetZ(vr);
			double clo = sdo.Ceiling.plane.GetZ(vl);
			double cro = sdo.Ceiling.plane.GetZ(vr);

			double fle = sd.Floor.plane.GetZ(vl);
			double fre = sd.Floor.plane.GetZ(vr);
			double cle = sd.Ceiling.plane.GetZ(vl);
			double cre = sd.Ceiling.plane.GetZ(vr);

			double fl = flo > fle ? flo : fle;
			double fr = fro > fre ? fro : fre;
			double cl = clo < cle ? clo : cle;
			double cr = cro < cre ? cro : cre;
			
			// Anything to see?
			if(((cl - fl) > 0.01f) || ((cr - fr) > 0.01f))
			{
				// Keep top and bottom planes for intersection testing
				top = extrafloor.Floor.plane;
				bottom = extrafloor.Ceiling.plane;
				
				// Create initial polygon, which is just a quad between floor and ceiling
				WallPolygon poly = new WallPolygon();
				poly.Add(new Vector3D(vl.x, vl.y, fl));
				poly.Add(new Vector3D(vl.x, vl.y, cl));
				poly.Add(new Vector3D(vr.x, vr.y, cr));
				poly.Add(new Vector3D(vr.x, vr.y, fr));
				
				// Determine initial color
				int lightlevel = lightabsolute ? lightvalue : sd.Ceiling.brightnessbelow + lightvalue;
				
				//mxd. This calculates light with doom-style wall shading
				PixelColor wallbrightness = PixelColor.FromInt(mode.CalculateBrightness(lightlevel, Sidedef));
				PixelColor wallcolor = PixelColor.Modulate(sd.Ceiling.colorbelow, wallbrightness);
				fogfactor = CalculateFogFactor(lightlevel);
				poly.color = wallcolor.WithAlpha(255).ToInt();

				// Cut off the part above the 3D floor and below the 3D ceiling
				CropPoly(ref poly, extrafloor.Floor.plane, false);
				CropPoly(ref poly, extrafloor.Ceiling.plane, false);

				// Cut out pieces that overlap 3D floors in this sector
				List<WallPolygon> polygons = new List<WallPolygon> { poly };
				bool translucent = (extrafloor.RenderAdditive || extrafloor.Alpha < 255);
				foreach(Effect3DFloor ef in sd.ExtraFloors)
				{
					// Effects can be created lazily during a retained-sector refresh.
					if(ef.Floor == null || ef.Ceiling == null) ef.Update();

					// Different translucent effects must coexist so the renderer can composite
					// them (for example a Solid in front of Water). Two solid-volume FOFs,
					// however, still merge so their shared/hidden interior walls are removed.
					// SRB2 maps often use one control sector with separate action lines/tags for
					// adjacent target sectors. Those are one continuous FOF even though their
					// Effect3DFloor instances and target tags differ.
					bool othertranslucent = (ef.RenderAdditive || ef.Alpha < 255);
					bool sameeffect = object.ReferenceEquals(extrafloor.Linedef, ef.Linedef);
					bool samewatervolume = extrafloor.IsWater && ef.IsWater;
					bool joinedvolume = AreJoinedAcrossBoundary(extrafloor, ef, vl, vr);
					if(translucent && othertranslucent
						&& !sameeffect && !samewatervolume && !joinedvolume
						&& !(extrafloor.MergeTranslucentSides && ef.MergeTranslucentSides)) continue;
					if(translucent && !othertranslucent && !ef.ClipSidedefs) continue;
					if(ef.ClipSidedefs == extrafloor.ClipSidedefs || ef.ClipSidedefs) 
					{
						int num = polygons.Count;
						for(int pi = 0; pi < num; pi++)
						{
							// Split by floor plane of 3D floor
							WallPolygon p = polygons[pi];
							WallPolygon np = SplitPoly(ref p, ef.Ceiling.plane, true);
							
							if(np.Count > 0)
							{
								// Split part below floor by the ceiling plane of 3D floor
								// and keep only the part below the ceiling (front)
								SplitPoly(ref np, ef.Floor.plane, true);

								if(p.Count == 0)
								{
									polygons[pi] = np;
								}
								else
								{
									polygons[pi] = p;
									polygons.Add(np);
								}
							}
							else
							{
								polygons[pi] = p;
							}
						}
					}
				}
				
				// Process the polygon and create vertices
				if(polygons.Count > 0)
				{
					List<WorldVertex> verts = CreatePolygonVertices(polygons, tp, sd, lightvalue, lightabsolute);
					if(verts.Count > 2)
					{
						if (extrafloor.Sloped3dFloor) this.RenderPass = RenderPass.Mask; //mxd
						else if (extrafloor.RenderAdditive) this.RenderPass = RenderPass.Additive; //mxd
						else if ((extrafloor.Alpha < 255) || Texture.IsTranslucent) this.RenderPass = RenderPass.Alpha; // [ZZ] translucent texture should trigger Alpha pass
						else this.RenderPass = RenderPass.Mask;

						if(extrafloor.Alpha < 255)
						{
							// Apply alpha to vertices
							byte alpha = (byte)General.Clamp(extrafloor.Alpha, 0, 255);
							if(alpha < 255)
							{
								for(int i = 0; i < verts.Count; i++)
								{
									WorldVertex v = verts[i];
									v.c = PixelColor.FromInt(v.c).WithAlpha(alpha).ToInt();
									verts[i] = v;
								}
							}
						}

						base.SetVertices(verts);
						return true;
					}
				}
			}
			
			base.SetVertices(null); //mxd
			return false;
		}
		
		#endregion
		
		#region ================== Methods

		private static bool AreJoinedAcrossBoundary(Effect3DFloor first, Effect3DFloor second,
			Vector2D left, Vector2D right)
		{
			if(first == null || second == null || first.Floor == null || first.Ceiling == null
				|| second.Floor == null || second.Ceiling == null || first.Linedef == null
				|| second.Linedef == null || first.Linedef.IsDisposed || second.Linedef.IsDisposed
				|| first.Linedef.Front == null || second.Linedef.Front == null) return false;

			// Ignore arg0: for SRB2 FOF actions it is the target-sector tag, which is
			// expected to differ on the two sides of a sector boundary.
			if(first.Linedef.Action != second.Linedef.Action
				|| !object.ReferenceEquals(first.Linedef.Front.Sector, second.Linedef.Front.Sector)
				|| first.Alpha != second.Alpha || first.RenderAdditive != second.RenderAdditive
				|| first.RenderInside != second.RenderInside) return false;

			for(int i = 1; i < first.Linedef.Args.Length && i < second.Linedef.Args.Length; i++)
				if(first.Linedef.Args[i] != second.Linedef.Args[i]) return false;

			const double epsilon = 0.01;
			return Math.Abs(first.Floor.plane.GetZ(left) - second.Floor.plane.GetZ(left)) <= epsilon
				&& Math.Abs(first.Floor.plane.GetZ(right) - second.Floor.plane.GetZ(right)) <= epsilon
				&& Math.Abs(first.Ceiling.plane.GetZ(left) - second.Ceiling.plane.GetZ(left)) <= epsilon
				&& Math.Abs(first.Ceiling.plane.GetZ(right) - second.Ceiling.plane.GetZ(right)) <= epsilon;
		}

		protected bool TryGetControlSide(out Sidedef sourceside)
		{
			sourceside = null;
			if(extrafloor == null || extrafloor.Linedef == null || extrafloor.Linedef.IsDisposed)
				return false;

			sourceside = extrafloor.Linedef.Front;
			return sourceside != null && !sourceside.IsDisposed && sourceside.Sector != null
				&& !sourceside.Sector.IsDisposed;
		}

		// This performs a fast test in object picking (mxd)
		public override bool PickFastReject(Vector3D from, Vector3D to, Vector3D dir) 
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return false;

			// Top and bottom are swapped in Vavoom-type 3d floors
			if(extrafloor.VavoomType)
				return (pickintersect.z >= top.GetZ(pickintersect)) && (pickintersect.z <= bottom.GetZ(pickintersect));
			return base.PickFastReject(from, to, dir);
		}

		//mxd. Alpha based picking
		public override bool PickAccurate(Vector3D from, Vector3D to, Vector3D dir, ref double u_ray)
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return false;
			if(!BuilderPlug.Me.AlphaBasedTextureHighlighting || !Texture.IsImageLoaded || (!Texture.IsTranslucent && !Texture.IsMasked)) return base.PickAccurate(from, to, dir, ref u_ray);

			double u;
			new Line2D(from, to).GetIntersection(Sidedef.Line.Line, out u);
			if(Sidedef != Sidedef.Line.Front) u = 1.0f - u;

			// Some textures (e.g. HiResImage) may lie about their size, so use bitmap size instead
            int imageWidth = Texture.GetAlphaTestWidth();
            int imageHeight = Texture.GetAlphaTestHeight();

            // Determine texture scale...
            Vector2D imgscale = new Vector2D((float)Texture.Width / imageWidth, (float)Texture.Height / imageHeight);
            Vector2D texscale = (Texture is HiResImage) ? imgscale * Texture.Scale : Texture.Scale;

			// Get correct offset to texture space...
			// Match SRB2's thick-side formula: the target wall supplies its normal
			// middle X offset and the FOF control side supplies a separate local X
			// contribution. The control side's classic X offset is not used here.
			double texoffsetx = Sidedef.OffsetX + UniFields.GetFloat(Sidedef.Fields, "offsetx_mid")
				+ UniFields.GetFloat(sourceside.Fields, "offsetx_mid");
            int ox = (int)Math.Floor((u * Sidedef.Line.Length * UniFields.GetFloat(sourceside.Fields, "scalex_mid", 1.0f) / texscale.x + (texoffsetx / imgscale.x)) % imageWidth);

			// SRB2 takes FOF-side Y entirely from the control side. Target-wall Y
			// belongs to a normal middle texture and must not affect alpha picking.
			double texoffsety = sourceside.OffsetY + UniFields.GetFloat(sourceside.Fields, "offsety_mid");
            int oy = (int)Math.Ceiling(((pickintersect.z - sourceside.Sector.CeilHeight) * UniFields.GetFloat(sourceside.Fields, "scaley_mid", 1.0f) / texscale.y - (texoffsety / imgscale.y)) % imageHeight);

            // Make sure offsets are inside of texture dimensions...
            if (ox < 0) ox += imageWidth;
            if (oy < 0) oy += imageHeight;

            // Check pixel alpha
            Point pixelpos = new Point(General.Clamp(ox, 0, imageWidth - 1), General.Clamp(imageHeight - oy, 0, imageHeight - 1));
            return (Texture.AlphaTestPixel(pixelpos.X, pixelpos.Y) && base.PickAccurate(@from, to, dir, ref u_ray));
		}

		// Return texture name
		public override string GetTextureName()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return "-";

			//mxd
			if((extrafloor.Linedef.Args[2] & (int)Effect3DFloor.Flags.UseUpperTexture) != 0)
				return Sidedef.HighTexture;
			if((extrafloor.Linedef.Args[2] & (int)Effect3DFloor.Flags.UseLowerTexture) != 0)
				return Sidedef.LowTexture;
			return sourceside.MiddleTexture;
		}

		// This changes the texture
		protected override void SetTexture(string texturename)
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;

			//mxd
			if((extrafloor.Linedef.Args[2] & (int)Effect3DFloor.Flags.UseUpperTexture) != 0)
				Sidedef.SetTextureHigh(texturename);
			else if((extrafloor.Linedef.Args[2] & (int)Effect3DFloor.Flags.UseLowerTexture) != 0)
				Sidedef.SetTextureLow(texturename);
			else
				sourceside.SetTextureMid(texturename);

			mode.RequestUsedTexturesUpdate();

			//mxd. Update model sector
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);
		}

		protected override void SetTextureOffsetX(int x)
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;

			// This is the FOF-only contribution used by SRB2 in addition to the
			// target wall's middle offset. Editing it leaves a normal midtexture on
			// the target wall untouched.
			sourceside.Fields.BeforeFieldsChange();
			sourceside.Fields["offsetx_mid"] = new UniValue(UniversalType.Float, (double)x);
		}

		protected override void SetTextureOffsetY(int y)
		{
			// SRB2 aligns the source side's Y offset instead of the in-level sidedef
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;

			sourceside.Fields.BeforeFieldsChange();
			sourceside.Fields["offsety_mid"] = new UniValue(UniversalType.Float, (double)y);
		}

		protected override void MoveTextureOffset(int offsetx, int offsety)
		{
			Sidedef sourceside;
			if(Sidedef == null || Sidedef.IsDisposed || !TryGetControlSide(out sourceside)) return;
			sourceside.Fields.BeforeFieldsChange();

			bool worldpanning = this.Texture.WorldPanning || General.Map.Data.MapInfo.ForceWorldPanning;
			double oldx = sourceside.Fields.GetValue("offsetx_mid", 0.0);
			double oldy = sourceside.Fields.GetValue("offsety_mid", 0.0);
			double scalex = sourceside.Fields.GetValue("scalex_mid", 1.0);
			double scaley = sourceside.Fields.GetValue("scaley_mid", 1.0);
			bool textureloaded = (Texture != null && Texture.IsImageLoaded); //mxd
			double width = textureloaded ? (worldpanning ? this.Texture.ScaledWidth / scalex : this.Texture.Width) : -1; // biwa
			double height = textureloaded ? (worldpanning ? this.Texture.ScaledHeight / scaley : this.Texture.Height) : -1; // biwa

			sourceside.Fields["offsetx_mid"] = new UniValue(UniversalType.Float, GetNewTexutreOffset(oldx, offsetx, width)); //mxd // biwa
			sourceside.Fields["offsety_mid"] = new UniValue(UniversalType.Float, GetNewTexutreOffset(oldy, offsety, height)); //mxd // biwa
		}

		protected override Point GetTextureOffset()
		{
			Sidedef sourceside;
			if(Sidedef == null || Sidedef.IsDisposed || !TryGetControlSide(out sourceside))
				return Point.Empty;

			// Both values are the FOF control-side contribution. SRB2 adds X to the
			// target wall's middle offset, while Y comes entirely from this side.
			double oldx = sourceside.Fields.GetValue("offsetx_mid", 0.0);
			double oldy = sourceside.Fields.GetValue("offsety_mid", 0.0);
			return new Point((int)oldx, (int)oldy);
		}

		//mxd
		public override Linedef GetControlLinedef()
		{
			Sidedef sourceside;
			return TryGetControlSide(out sourceside) ? extrafloor.Linedef : null;
		}

		public override void OnSelectBegin()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;
			base.OnSelectBegin();
		}

		public override void OnSelectEnd()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside))
			{
				mode.UnlockTarget();
				return;
			}

			base.OnSelectEnd();
		}

		//mxd
		public override void OnTextureFit(FitTextureOptions options) 
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;
			if(!General.Map.UDMF) return;
			if(string.IsNullOrEmpty(sourceside.MiddleTexture) || sourceside.MiddleTexture == "-" || !Texture.IsImageLoaded) return;
			FitTexture(options);

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);
		}

		//mxd. Only control sidedef scale is used by GZDoom
		public override void OnChangeScale(int incrementX, int incrementY)
		{
			if(!General.Map.UDMF || Texture == null || !Texture.IsImageLoaded) return;
			Sidedef target;
			if(!TryGetControlSide(out target)) return;

			if((General.Map.UndoRedo.NextUndo == null) || (General.Map.UndoRedo.NextUndo.TicketID != undoticket))
				undoticket = mode.CreateUndo("Change wall scale");

			double scaleX = target.Fields.GetValue("scalex_mid", 1.0);
			double scaleY = target.Fields.GetValue("scaley_mid", 1.0);

			target.Fields.BeforeFieldsChange();

			if(incrementX != 0) UniFields.SetFloat(target.Fields, "scalex_mid", BuilderModesTools.AdjustTextureScale(scaleX, incrementX), 1.0);
			if(incrementY != 0) UniFields.SetFloat(target.Fields, "scaley_mid", BuilderModesTools.AdjustTextureScale(scaleY, incrementY), 1.0);

			scaleX = target.Fields.GetValue("scalex_mid", 1.0);
			scaleY = target.Fields.GetValue("scaley_mid", 1.0);

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(target.Sector).UpdateSectorGeometry(false);

			// Display result
			mode.SetActionResult("Wall scale changed to " + scaleX.ToString("F04", CultureInfo.InvariantCulture) + ", " + scaleY.ToString("F04", CultureInfo.InvariantCulture) + " (" + (int)Math.Round(Texture.Width / scaleX) + " x " + (int)Math.Round(Texture.Height / scaleY) + ").");
		}

		//mxd
		protected override void ResetTextureScale()
		{
			Sidedef target;
			if(!TryGetControlSide(out target)) return;
			target.Fields.BeforeFieldsChange();
			if(target.Fields.ContainsKey("scalex_mid")) target.Fields.Remove("scalex_mid");
			if(target.Fields.ContainsKey("scaley_mid")) target.Fields.Remove("scaley_mid");
		}

		//mxd
		public override void OnResetTextureOffset()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;
			mode.CreateUndo("Reset FOF texture offsets");
			mode.SetActionResult("FOF offsets reset. Target midtexture unchanged.");

			// SRB2 ignores the control side's classic X offset for FOF walls. Do not
			// reset it because it may belong to a normal wall in the control sector.
			sourceside.OffsetY = 0;

			// Reset texture offsets too
			SetTextureOffsetX(0);
			SetTextureOffsetY(0);

			// Update sidedef geometry
			VisualSidedefParts parts = Sector.GetSidedefParts(Sidedef);
			parts.SetupAllParts();

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);
		}

		//mxd
		public override void OnResetLocalTextureOffset()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;
			if(!General.Map.UDMF || !General.Map.Config.UseLocalSidedefTextureOffsets)
			{
				OnResetTextureOffset();
				return;
			}

			base.OnResetLocalTextureOffset();

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);
		}

		//sphere: update FOF texture offsets and skewing properly
		public override bool OnChangeTextureOffset(int horizontal, int vertical, bool doSurfaceAngleCorrection)
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return false;
			base.OnChangeTextureOffset(horizontal, vertical, doSurfaceAngleCorrection);
			Point offset = GetTextureOffset();
			mode.SetActionResult("Changed FOF offsets to " + offset.X + ", " + offset.Y
				+ ". Target midtexture unchanged.");

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);

			return true;
		}
		public override void OnToggleSlopeSkew()
		{
			Sidedef sourceside;
			if(!TryGetControlSide(out sourceside)) return;
			base.OnToggleSlopeSkew();

			// Update the model sector to update all 3d floors
			mode.GetVisualSector(sourceside.Sector).UpdateSectorGeometry(false);
		}

		#endregion
	}
}
