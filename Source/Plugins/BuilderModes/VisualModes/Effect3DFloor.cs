#region === Copyright (c) 2010 Pascal van der Heiden ===

using System;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal class Effect3DFloor : SectorEffect
	{
		// Linedef that is used to create this effect
		// The sector can be found by linedef.Front.Sector
		private readonly Linedef linedef;
		
		// Floor and ceiling planes
        public PixelColor ColorFloor { get; private set; }
		private SectorLevel floor;
        public PixelColor ColorCeiling { get; private set; }
		private SectorLevel ceiling;

		// Alpha transparency
		private int alpha;
		
		// Vavoom type?
		private bool vavoomtype;

		//mxd. Render backsides?
		private bool renderinside;

		//mxd. Dirty hack to emulate GZDoom behaviour?
		private bool sloped3dfloor;

		//mxd. Render using Additive pass?
		private bool renderadditive;

		//mxd. Sidedef should be clipped by floor/ceiling?
		private bool clipsides;

		// Overlapping translucent FOF sides should be merged when both effects
		// represent solid volumes. Non-solid effects (water, fog, etc.) must stay
		// separate so the renderer can composite the surfaces behind them.
		private bool mergetranslucentsides;

		// Some SRB2 FOF actions can explicitly suppress their vertical walls while
		// retaining the top/bottom planes. This is independent of RenderInside.
		private bool rendersides;
		private bool water;

		//mxd. Ignore Bottom Height?
		private bool ignorebottomheight;

		// Properties
		public int Alpha { get { return alpha; } }
		public SectorLevel Floor { get { return floor; } }
		public SectorLevel Ceiling { get { return ceiling; } }
		public Linedef Linedef { get { return linedef; } }
		public bool VavoomType { get { return vavoomtype; } }
		public bool RenderInside { get { return renderinside; } } //mxd
		public bool RenderAdditive { get { return renderadditive; } } //mxd
		public bool IgnoreBottomHeight { get { return ignorebottomheight; } } //mxd
		public bool Sloped3dFloor { get { return sloped3dfloor; } } //mxd
		public bool ClipSidedefs { get { return clipsides; } } //mxd
		public bool MergeTranslucentSides { get { return mergetranslucentsides; } }
		public bool RenderSides { get { return rendersides; } }
		public bool IsWater { get { return water; } }

		//mxd. 3D-Floor Flags
		[Flags]
		public enum Flags
		{
			None = 0,
			DisableLighting = 1,
			RestrictLighting = 2,
			Fog = 4,
			IgnoreBottomHeight = 8,
			UseUpperTexture = 16,
			UseLowerTexture = 32,
			RenderAdditive = 64,
			Fade = 512,
			ResetLighting = 1024,
		}

		//mxd. 3D-Floor Types
		[Flags]
		public enum FloorTypes
		{
			VavoomStyle = 0,
			Solid = 1,
			Swimmable = 2,
			NonSolid = 3,
			RenderInside = 4,
			HiTagIsLineID = 8,
			InvertVisibilityRules = 16,
			InvertShootabilityRules = 32
		}
		
		// Constructor
		public Effect3DFloor(SectorData data, Linedef sourcelinedef) : base(data)
		{
			linedef = sourcelinedef;
			
			// New effect added: This sector needs an update!
			if(data.Mode.VisualSectorExists(data.Sector))
			{
				BaseVisualSector vs = (BaseVisualSector)data.Mode.GetVisualSector(data.Sector);
				vs.UpdateSectorGeometry(true);
			}
		}

		// This makes sure we are updated with the source linedef information
		public override void Update()
		{
			SectorData sd = data.Mode.GetSectorData(linedef.Front.Sector);
			if(!sd.Updated) sd.Update();
			sd.AddUpdateSector(data.Sector, true);

			if(floor == null)
			{
				floor = new SectorLevel(sd.Floor);
				data.AddSectorLevel(floor);
			}

			if(ceiling == null)
			{
				ceiling = new SectorLevel(sd.Ceiling);
				data.AddSectorLevel(ceiling);
			}

			alpha = 255;
			vavoomtype = false;
			renderinside = false;
			renderadditive = false;
			ignorebottomheight = false;
			mergetranslucentsides = false;
			rendersides = true;
			water = false;

			string actionid = General.Map.Config.LinedefActions[linedef.Action].Id.ToLowerInvariant();
			switch (actionid)
			{
				case "srb2_fofsolid":
					mergetranslucentsides = true;
					rendersides = (linedef.Args[3] & 2) == 0;
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderinside = (linedef.Args[3] & 4) == 4;
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_fofwater":
					water = true;
					rendersides = (linedef.Args[3] & 1) == 0;
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderinside = true;
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_fofcrumbling":
					mergetranslucentsides = true;
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderinside = (linedef.Args[3] & 7) != 0;
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_foflight":
					alpha = 0;
					ignorebottomheight = linedef.Args[1] == 1;
					break;
				case "srb2_fofintangibleinvisible":
				case "srb2_foffog":
					alpha = 0;
					break;
				case "srb2_fofintangible":
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderinside = (linedef.Args[3] & 4) != 4;
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_fofbustable":
					mergetranslucentsides = true;
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_foflaser":
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderadditive = linedef.Args[2] == 1;
					break;
				case "srb2_fofcustom":
					mergetranslucentsides = (linedef.Args[3] & 256) == 256; // Cut extra walls
					alpha = General.Clamp(linedef.Args[1], 0, 255);
					renderinside = (linedef.Args[3] & 1024) == 1024;
					renderadditive = linedef.Args[2] == 1;
					break;
				case "sector_set3dfloor":
					vavoomtype = linedef.Args[1] == (int)FloorTypes.VavoomStyle;
					mergetranslucentsides = vavoomtype || (linedef.Args[1] & 3) == (int)FloorTypes.Solid;
					// For non-vavoom types, we must switch the level types
					if (!vavoomtype)
					{
						//mxd. check for Swimmable/RenderInside/RenderAdditive flags
						renderadditive = (linedef.Args[2] & (int)Flags.RenderAdditive) == (int)Flags.RenderAdditive;
						renderinside = ((((linedef.Args[1] & (int)FloorTypes.Swimmable) == (int)FloorTypes.Swimmable) && (linedef.Args[1] & (int)FloorTypes.NonSolid) != (int)FloorTypes.NonSolid))
									  || ((linedef.Args[1] & (int)FloorTypes.RenderInside) == (int)FloorTypes.RenderInside);
						ignorebottomheight = ((linedef.Args[2] & (int)Flags.IgnoreBottomHeight) == (int)Flags.IgnoreBottomHeight);
				
						alpha = General.Clamp(linedef.Args[3], 0, 255);
					}
					break;
				case "srb2_fofsolidopaque":
					mergetranslucentsides = true;
					break;
			}

			// SRB2 deliberately gives several FOF types no visible surfaces. Show those
			// volumes at the user's editor-only opacity while previews are enabled, including
			// zero-alpha variants and the invisible Mario block. The source action and
			// arguments remain untouched, so testing the map still matches SRB2.
			bool invisiblemarioblock = actionid == "srb2_fofsolidopaque"
				&& linedef.Action == 250 && linedef.Args[1] == 2;
			if(General.Settings.SRB2RenderingEffects
				&& actionid.StartsWith("srb2_fof", StringComparison.Ordinal)
				&& (alpha == 0 || invisiblemarioblock))
			{
				alpha = linedef.GetSRB2InvisibleFOFPreviewAlpha();
			}

			if (!vavoomtype)
			{
				sd.Ceiling.CopyProperties(floor);
				sd.Floor.CopyProperties(ceiling);
				floor.type = SectorLevelType.Floor;
				floor.plane = sd.Ceiling.plane.GetInverted();
				ceiling.type = SectorLevelType.Ceiling;
				ceiling.plane = (ignorebottomheight ? sd.Ceiling.plane : sd.Floor.plane.GetInverted()); //mxd. Use upper plane when "ignorebottomheight" flag is set

				// A 3D floor's color is always that of the sector it is placed in
				// (unless it's affected by glow) - mxd
				if (sd.CeilingGlow == null || !sd.CeilingGlow.Fullbright) floor.color = 0;
			}
			else
			{
				floor.type = SectorLevelType.Ceiling;
				floor.plane = sd.Ceiling.plane;
				ceiling.type = SectorLevelType.Floor;
				ceiling.plane = sd.Floor.plane;

				// A 3D floor's color is always that of the sector it is placed in
				// (unless it's affected by glow) - mxd
				if (sd.FloorGlow == null || !sd.FloorGlow.Fullbright) ceiling.color = 0;
			}

			//mxd
			clipsides = (!renderinside && !renderadditive && alpha > 254 && !ignorebottomheight);

			// Apply alpha
			floor.alpha = alpha;
			ceiling.alpha = alpha;

			//mxd
			floor.extrafloor = this;
			ceiling.extrafloor = this;
			floor.splitsides = !clipsides;
			ceiling.splitsides = (!clipsides && !ignorebottomheight); // if "ignorebottomheight" flag is set, both ceiling and floor will be at the same level and sidedef clipping with floor level will fail resulting in incorrect light props transfer in some cases

			sloped3dfloor = false;

			bool disablelighting = false;
			bool restrictlighting = false;
			floor.resetlighting = false;

			switch (actionid)
			{
				case "srb2_fofsolid":
					disablelighting = (linedef.Args[3] & 16) == 16;
					break;
				case "srb2_fofwater":
					restrictlighting = (linedef.Args[3] & 2) == 2;
					break;
				case "srb2_fofcrumbling":
					disablelighting = (linedef.Args[4] & 1) == 1;
					break;
				case "srb2_foflight":
					restrictlighting = linedef.Args[1] == 0;
					break;
				case "srb2_foffog":
				case "srb2_fofintangibleinvisible":
				case "srb2_foflaser":
					disablelighting = true;
					break;
				case "srb2_fofintangible":
					disablelighting = (linedef.Args[3] & 16) == 16;
					break;
				case "srb2_fofcustom":
					disablelighting = (linedef.Args[3] & 64) == 64;
					restrictlighting = (linedef.Args[3] & 131072) == 131072;
					break;
				case "sector_set3dfloor":
					// Do not adjust light? (works only for non-vavoom types)
					if (!vavoomtype)
					{
						disablelighting = ((linedef.Args[2] & (int)Flags.DisableLighting) == (int)Flags.DisableLighting); //mxd
						restrictlighting = ((linedef.Args[2] & (int)Flags.RestrictLighting) == (int)Flags.RestrictLighting); //mxd
						floor.resetlighting = ((linedef.Args[2] & (int)Flags.ResetLighting) == (int)Flags.ResetLighting); //mxd
					}
					//mxd. Check slopes, cause GZDoom can't handle sloped translucent 3d floors...
					sloped3dfloor = ((alpha < 255 || renderadditive) &&
									 (Angle2D.RadToDeg(ceiling.plane.Normal.GetAngleZ()) != 270 ||
									  Angle2D.RadToDeg(floor.plane.Normal.GetAngleZ()) != 90));
					break;
			}

			if (disablelighting || restrictlighting)
			{
				floor.restrictlighting = restrictlighting; //mxd
				floor.disablelighting = disablelighting; //mxd

				if (disablelighting) //mxd
				{
					floor.color = 0;
					floor.brightnessbelow = -1;
					floor.colorbelow = PixelColor.FromInt(0);
				}

				ceiling.disablelighting = disablelighting; //mxd
				ceiling.restrictlighting = restrictlighting; //mxd

				ceiling.color = 0;
				ceiling.brightnessbelow = -1;
				ceiling.colorbelow = PixelColor.FromInt(0);
			}

			if (vavoomtype)
			{
				ColorFloor = sd.ColorCeiling;
				ColorCeiling = sd.ColorFloor;
			}
			else
			{
				ColorFloor = sd.ColorFloor;
				ColorCeiling = sd.ColorCeiling;
			}
		}
	}
}
