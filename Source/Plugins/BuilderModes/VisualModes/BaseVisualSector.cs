
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
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.VisualModes;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal class BaseVisualSector : VisualSector
	{
		#region ================== Constants

		#endregion

		#region ================== Variables
		
		private readonly BaseVisualMode mode;

		private VisualFloor floor;
		private VisualCeiling ceiling;
		private List<VisualFloor> extrafloors;
		private List<VisualCeiling> extraceilings;
		private readonly List<VisualFloor> extrabackfloors; //mxd
		private readonly List<VisualCeiling> extrabackceilings; //mxd
		private Dictionary<Sidedef, VisualSidedefParts> sides;
		
		// If this is set to true, the sector will be rebuilt after the action is performed.
		private bool changed;
		
		// Prevent recursion
		private bool isupdating;

		#endregion

		#region ================== Properties
		
		public VisualFloor Floor { get { return floor; } }
		public VisualCeiling Ceiling { get { return ceiling; } }
		public List<VisualFloor> ExtraFloors { get { return extrafloors; } }
		public List<VisualCeiling> ExtraCeilings { get { return extraceilings; } }
		public List<VisualFloor> ExtraBackFloors { get { return extrabackfloors; } } //mxd
		public List<VisualCeiling> ExtraBackCeilings { get { return extrabackceilings; } } //mxd
		public Dictionary<Sidedef, VisualSidedefParts> Sides { get { return sides; } } //mxd
		public bool Changed { get { return changed; } set { changed |= value; } }
		
		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public BaseVisualSector(BaseVisualMode mode, Sector s) : base(s)
		{
			this.mode = mode;
			this.extrafloors = new List<VisualFloor>(2);
			this.extraceilings = new List<VisualCeiling>(2);
			this.extrabackfloors = new List<VisualFloor>(2); //mxd
			this.extrabackceilings = new List<VisualCeiling>(2); //mxd
			this.sides = new Dictionary<Sidedef, VisualSidedefParts>(s.Sidedefs.Count);
			
			// Initialize
			Rebuild();
			
			// We have no destructor
			GC.SuppressFinalize(this);
		}

		// Disposer
		public override void Dispose()
		{
			// Not already disposed?
			if(!IsDisposed)
			{
				// Clean up
				sides = null;
				floor = null;
				ceiling = null;
				extrafloors = null;
				extraceilings = null;
				
				// Dispose base
				base.Dispose();
			}
		}

		#endregion

		#region ================== Methods
		
		// This retreives the sector data for this sector
		public SectorData GetSectorData()
		{
			return mode.GetSectorData(this.Sector);
		}
		
		// This updates this virtual the sector and neightbours if needed
		override public void UpdateSectorGeometry(bool includeneighbours)
		{
			if(isupdating) return;
				
			isupdating = true;
			changed = true;
			
			// Not sure what from this part we need, so commented out for now
			SectorData data = mode.GetSectorDataEx(this.Sector); //mxd
			if(data != null) //mxd
			{
				data.Reset(false);

				// Update sectors that rely on this sector
				foreach(KeyValuePair<Sector, bool> s in data.UpdateAlso)
				{
					SectorData other = mode.GetSectorDataEx(s.Key);
					if(other != null) other.Reset(s.Value);
				}
			}
			
			// Update only materialized visual things in this sector. Scanning the
			// entire map here made small sector edits increasingly expensive.
			mode.UpdateVisualThingsInSector(this.Sector, false);

			if(includeneighbours)
			{
				// Also rebuild surrounding sectors, because outside sidedefs may need to be adjusted
				foreach(Sidedef sd in this.Sector.Sidedefs)
				{
					if(sd.Other != null)
					{
						if(mode.VisualSectorExists(sd.Other.Sector))
						{
							BaseVisualSector vs = (BaseVisualSector)mode.GetVisualSector(sd.Other.Sector);
							SectorData other = mode.GetSectorDataEx(sd.Other.Sector);

							if(other != null)
							{
								other.Reset(other.UpdateAlso.Count > 0 ? true : false); // biwa. Make sure to reset the update status of dependend sectors. This fixes #250, where 3D floors where not updated when the control sector was sloped using line action 181
							}

							// Resetting SectorData is not enough: the neighbouring visual sector
							// owns the opposite wall face. Mark it for rebuild so height changes
							// immediately update exposed walls and their F_SKY1/missing-texture
							// state from either camera side.
							vs.Changed = true;
						}
					}
				}
			}

			Sector.UpdateFogColor(); //mxd
			isupdating = false;
		}

		//mxd. call this to update sector and things in it when Sector.Fields are changed 
		override public void UpdateSectorData() 
		{
			//update sector data
			SectorData data = GetSectorData();
			data.UpdateForced();

			//update sector
			Rebuild();

			//update materialized things in this sector
			mode.UpdateVisualThingsInSector(this.Sector, true);
		}
		
		// This (re)builds the visual sector, calculating all geometry from scratch
		public void Rebuild()
		{
			long rebuildstart = Clock.CurrentTime;
			Dictionary<Sidedef, VisualSidedefParts> oldsides = sides ?? new Dictionary<Sidedef, VisualSidedefParts>(1);

			// Forget old geometry
			base.ClearGeometry();
			
			// Get sector data
			SectorData data = GetSectorData();
			if(!data.Updated) data.Update();
			long dataready = Clock.CurrentTime;
			
			// Create floor
			floor = (floor ?? new VisualFloor(mode, this));
			if(floor.Setup(data.Floor, null)) AddGeometry(floor);
			
			// Create ceiling
			ceiling = (ceiling ?? new VisualCeiling(mode, this));
			if(ceiling.Setup(data.Ceiling, null)) AddGeometry(ceiling);
			long planesready = Clock.CurrentTime;
			
			// Create 3D floors
			for(int i = 0; i < data.ExtraFloors.Count; i++)
			{
				Effect3DFloor ef = data.ExtraFloors[i];
				bool floorRequired = ef.VavoomType; //mxd
				bool ceilingRequired = ef.VavoomType; //mxd

				if(ef.VavoomType || !ef.IgnoreBottomHeight) 
				{
					//mxd. check if 3d floor is between real floor and ceiling
					if(!ef.VavoomType) 
					{
						if(ef.Ceiling.plane.GetInverted().Normal != floor.Level.plane.Normal
							|| ef.Ceiling.plane.Normal != ceiling.Level.plane.Normal) 
						{
							//mxd. check if at least one vertex of 3d floor is between floor and ceiling
							floorRequired = Check3dFloorPlane(floor.Vertices, ceiling.Vertices, ef.Ceiling.plane);
						
						} 
						//if floor, ceiling and 3d floor are not sloped, compare offsets
						else if(-floor.Level.plane.Offset < ef.Ceiling.plane.Offset
							&& ceiling.Level.plane.Offset > ef.Ceiling.plane.Offset) 
						{
							floorRequired = true;
						}
					}

					//mxd. Create a floor
					if(floorRequired && ef.Ceiling.sector.IsDisposed == false) 
					{
						VisualFloor vf = (i < extrafloors.Count) ? extrafloors[i] : new VisualFloor(mode, this);
						if(vf.Setup(ef.Ceiling, ef)) 
						{
							base.AddGeometry(vf);

							//mxd. add backside as well
							if(!ef.VavoomType && ef.RenderInside) 
							{
								VisualFloor vfb = (i < extrabackfloors.Count) ? extrabackfloors[i] : new VisualFloor(mode, this);
								if(vfb.Setup(ef.Ceiling, ef, true)) base.AddGeometry(vfb);
								if(i >= extrabackfloors.Count) extrabackfloors.Add(vfb);
							}
						}
						if(i >= extrafloors.Count) extrafloors.Add(vf);
					}
				}

				//mxd. check if 3d ceiling is between real floor and ceiling
				if(!ef.VavoomType) 
				{
					if(ef.Floor.plane.GetInverted().Normal != ceiling.Level.plane.Normal
						|| ef.Floor.plane.Normal != floor.Level.plane.Normal) 
					{
						//mxd. check if at least one vertex of 3d ceiling is between floor and ceiling
						ceilingRequired = Check3dFloorPlane(floor.Vertices, ceiling.Vertices, ef.Floor.plane);
					
					}
					//if floor, ceiling and 3d ceiling are not sloped, compare offsets
					else if(ceiling.Level.plane.Offset > -ef.Floor.plane.Offset
						&& floor.Level.plane.Offset > ef.Floor.plane.Offset) 
					{
						ceilingRequired = true;
					}
				}

				//mxd. Create a ceiling
				if(ceilingRequired && ef.Floor.sector.IsDisposed == false) 
				{
					VisualCeiling vc = (i < extraceilings.Count) ? extraceilings[i] : new VisualCeiling(mode, this);
					if(vc.Setup(ef.Floor, ef)) 
					{
						base.AddGeometry(vc);

						//mxd. add backside as well
						if(!ef.VavoomType && (ef.RenderInside || ef.IgnoreBottomHeight)) 
						{
							VisualCeiling vcb = (i < extrabackceilings.Count) ? extrabackceilings[i] : new VisualCeiling(mode, this);
							if(vcb.Setup(ef.Floor, ef, true)) base.AddGeometry(vcb);
							if(i >= extrabackceilings.Count) extrabackceilings.Add(vcb);
						}
					}
					if(i >= extraceilings.Count) extraceilings.Add(vc);
				}
			}
			long extrafloorsready = Clock.CurrentTime;

			// Build every wall while the sector data is current. Deferring individual
			// wall creation until it entered the camera caused a visible blank frame and
			// allowed old lighting/offset data to survive until that wall was aimed at.
			// Reuse the existing wall objects where possible so rebuilds still avoid
			// unnecessary allocations.
			sides = new Dictionary<Sidedef, VisualSidedefParts>(base.Sector.Sidedefs.Count);
			foreach(Sidedef sd in base.Sector.Sidedefs)
			{
				VisualSidedefParts parts;
				if(!oldsides.TryGetValue(sd, out parts)) parts = new VisualSidedefParts();
				BuildSidedefGeometry(sd, parts, data);
			}
			
			// Done
			changed = false;

			long total = Clock.CurrentTime - rebuildstart;
			if(total >= 50)
			{
				General.WriteLogLine("Slow visual sector details: sector " + Sector.Index + ", data "
					+ (dataready - rebuildstart) + " ms, floor/ceiling " + (planesready - dataready)
					+ " ms, 3D floors " + (extrafloorsready - planesready) + " ms, built sides "
					+ (Clock.CurrentTime - extrafloorsready) + " ms; " + sides.Count
					+ " built of " + Sector.Sidedefs.Count + " sides, "
					+ data.ExtraFloors.Count + " 3D floors");
			}
		}

		private void BuildSidedefGeometry(Sidedef sd, VisualSidedefParts parts, SectorData data)
		{
			if(sd.Other != null && sd.Line.IsFlagSet(General.Map.Config.DoubleSidedFlag))
			{
				VisualUpper vu = parts.upper ?? new VisualUpper(mode, this, sd);
				if(vu.Setup()) base.AddGeometry(vu);

				VisualLower vl = parts.lower ?? new VisualLower(mode, this, sd);
				if(vl.Setup()) base.AddGeometry(vl);

				VisualMiddleDouble vm = parts.middledouble ?? new VisualMiddleDouble(mode, this, sd);
				if(vm.Setup()) base.AddGeometry(vm);

				VisualFogBoundary vb = parts.fogboundary ?? new VisualFogBoundary(mode, this, sd);
				if(vb.Setup())
				{
					vm.FogFactor = 0;
					base.AddGeometry(vb);
				}

				SectorData osd = mode.GetSectorData(sd.Other.Sector);
				if(!osd.Updated) osd.Update();
				List<VisualMiddle3D> middles = parts.middle3d ?? new List<VisualMiddle3D>(osd.ExtraFloors.Count);
				for(int i = 0; i < osd.ExtraFloors.Count; i++)
				{
					Effect3DFloor ef = osd.ExtraFloors[i];
					if(!ef.VavoomType && ef.IgnoreBottomHeight) continue;

					VisualMiddle3D vm3 = (i < middles.Count) ? middles[i] : new VisualMiddle3D(mode, this, sd);
					if(vm3.Setup(ef)) base.AddGeometry(vm3);
					if(i >= middles.Count) middles.Add(vm3);
				}

				List<VisualMiddleBack> middlebacks = parts.middleback ?? new List<VisualMiddleBack>();
				middlebacks.Clear();
				for(int i = 0; i < data.ExtraFloors.Count; i++)
				{
					Effect3DFloor ef = data.ExtraFloors[i];
					if(!ef.VavoomType && ef.RenderInside && !ef.IgnoreBottomHeight)
					{
						VisualMiddleBack vms = new VisualMiddleBack(mode, this, sd);
						if(vms.Setup(ef)) base.AddGeometry(vms);
						middlebacks.Add(vms);
					}
				}

				sides.Add(sd, new VisualSidedefParts(vu, vl, vm, vb, middles, middlebacks));
			}
			else
			{
				VisualMiddleSingle vm = parts.middlesingle ?? new VisualMiddleSingle(mode, this, sd);
				if(vm.Setup()) base.AddGeometry(vm);
				sides.Add(sd, new VisualSidedefParts(vm));
			}
		}

		public override bool SidedefGeometryReady(Sidedef sd)
		{
			return sides != null && sd != null && sides.ContainsKey(sd);
		}

		public override void PrepareSidedefGeometry(Sidedef sd)
		{
			if(IsDisposed || sd == null || sd.IsDisposed || sd.Sector != Sector || SidedefGeometryReady(sd)) return;

			long start = Clock.CurrentTime;
			SectorData data = GetSectorData();
			if(!data.Updated) data.Update();
			long dataready = Clock.CurrentTime;
			BuildSidedefGeometry(sd, new VisualSidedefParts(), data);
			long elapsed = Clock.CurrentTime - start;
			if(elapsed >= 25)
				General.WriteLogLine("Slow Visual Mode sidedef build: sidedef " + sd.Index + " in sector "
					+ Sector.Index + " took " + elapsed + " ms (sector data " + (dataready - start)
					+ " ms, wall parts " + (Clock.CurrentTime - dataready) + " ms, two-sided "
					+ (sd.Other != null) + ", local 3D floors " + data.ExtraFloors.Count + ")");
		}

		// Re-register every currently valid part of one wall. SetupAllParts can update
		// an existing surface, but it cannot insert a middle texture whose old setup had
		// zero triangles into VisualSector's draw list (or remove one that vanished).
		public void RefreshSidedefGeometry(Sidedef sd)
		{
			if(IsDisposed || sd == null || sd.IsDisposed || sd.Sector != Sector) return;

			VisualSidedefParts parts;
			if(!sides.TryGetValue(sd, out parts))
			{
				PrepareSidedefGeometry(sd);
				return;
			}

			base.RemoveSidedefGeometry(sd);
			sides.Remove(sd);
			SectorData data = GetSectorData();
			if(!data.Updated) data.Update();
			BuildSidedefGeometry(sd, parts, data);
		}

		public void PrepareAllSidedefGeometry()
		{
			foreach(Sidedef sd in Sector.Sidedefs) PrepareSidedefGeometry(sd);
		}
		
		// This returns the visual sidedef parts for a given sidedef
		public VisualSidedefParts GetSidedefParts(Sidedef sd)
		{
			return (sides.ContainsKey(sd) ? sides[sd] : new VisualSidedefParts());
		}

		//mxd. Checks if given plane is between given floor and ceiling vertices
		private static bool Check3dFloorPlane(WorldVertex[] floorverts, WorldVertex[] ceilingverts, Plane plane) 
		{
			bool show = false;

			//check floor
			for(int c = 0; c < floorverts.Length; c++) 
			{
				if(plane.GetZ(new Vector2D(floorverts[c].x, floorverts[c].y)) > Math.Round(floorverts[c].z, 3)) 
				{
					show = true;
					break;
				}
			}

			if(!show) return false;

			//check ceiling
			for(int c = 0; c < ceilingverts.Length; c++) 
			{
				if(plane.GetZ(new Vector2D(ceilingverts[c].x, ceilingverts[c].y)) < Math.Round(ceilingverts[c].z, 3))
					return true;
			}

			return false;
		}
		
		#endregion
	}
}
