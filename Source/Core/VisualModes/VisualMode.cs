
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
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Editing;

#endregion

namespace CodeImp.DoomBuilder.VisualModes
{
	public enum PickingMode
	{
		Default,
		SidedefSlopeHandles,
		VertexSlopeHandles
	}

	/// <summary>
	/// Provides specialized functionality for a visual (3D) Doom Builder editing mode.
	/// </summary>
	public abstract class VisualMode : EditMode
	{
		#region ================== Constants

		private const double MOVE_SPEED_MULTIPLIER = 0.001;
		protected const float PICK_RANGE = 0.98f;
		private const float MOVE_CAMERA_DISTANCE = 64.0f;
		private const int NORMAL_CULL_INTERVAL_MS = 16;
		private const int LARGE_VIEW_CULL_INTERVAL_MS = 33;
		private const float LARGE_VIEW_DISTANCE = 16000.0f;
		
		#endregion

		#region ================== Variables
		
		// Graphics
		protected IRenderer3D renderer;
		
		// Options
		private bool processgeometry;
		private bool processthings;
		
		// Mouse status
		protected Vector2D mousepos;
		protected Vector3D mouseworlddir;

		// Input
		private bool keyforward;
		private bool keybackward;
		private bool keyleft;
		private bool keyright;
		private bool keyup;
		private bool keydown;
		private bool orbit;
		protected bool cursorunlocked;

		//mxd
		private List<VisualThing> selectedVisualThings;
		private List<VisualSector> selectedVisualSectors;
		protected Dictionary<Vertex, VisualVertexPair> vertices;
		private Vector2D initialcameraposition;
		//used in "Play From Here" Action
		private Thing playerStart;
		private Vector3D playerStartPosition;
		private double playerStartAngle;

		private Vector3D orbitPoint;
		private double orbitXY;
		private double orbitZ;
		private double orbitRadius;
		private bool orbitPointPicked;

		// For picking
		protected PickingMode pickingmode;

		// Map
		protected VisualBlockMap blockmap;
		protected Dictionary<Thing, VisualThing> allthings;
		protected Dictionary<Sector, VisualSector> allsectors;
		protected Dictionary<Sector, List<VisualSlope>> allslopehandles;
		protected Dictionary<Sector, List<VisualSlope>> sidedefslopehandles;
		protected Dictionary<Sector, List<VisualSlope>> vertexslopehandles;
		protected List<VisualBlockEntry> visibleblocks;
		protected List<VisualThing> visiblethings;
		protected List<VisualSector> visiblesectors;
		protected List<VisualGeometry> visiblegeometry;
		private List<VisualBlockEntry> candidatevisibleblocks;
		protected List<VisualBlockEntry> skyboxvisibleblocks;
		protected List<VisualThing> skyboxvisiblethings;
		protected List<VisualGeometry> skyboxvisiblegeometry;
		private List<VisualBlockEntry> skyboxcandidatevisibleblocks;
		protected HashSet<VisualSlope> usedslopehandles;

		// Reused culling/picking scratch collections. Visual mode runs these paths
		// continuously, and allocating them every pass causes avoidable GC stalls on
		// large maps.
		protected List<Linedef> visiblelines;
		private List<VisualBlockEntry> pickblocks;
		private HashSet<Linedef> picklines;
		private HashSet<Sector> picksectors;
		private HashSet<Thing> pickthings;
		private HashSet<Vertex> pickvertices;
		private List<IVisualPickable> pickables;
		private List<IVisualPickable> potentialpicks;
		private List<VisualBlockEntry> sectorqueryblocks;
		private List<Sector> sectorquerysectors;
		private HashSet<Linedef> sectorquerylinedefs;
		private HashSet<Linedef> cullmovementlines;
		private HashSet<VisualBlockEntry> blockmemberscratch;
		private bool blockmapiscurrent;
		private long blockmapchangeversion;
		private Vector2D lastsectorqueryposition;
		private bool haslastsectorqueryposition;
		private Vector3D lastcullcameraposition;
		private double lastcullcameraanglexy;
		private double lastcullcameraanglez;
		private long lastculltime;
		private long lastcullcost;
		private bool haslastcullcamera;
		private Vector2D lastskyboxvisibilityposition;
		private bool hasskyboxvisibility;
		private long lastskyboxvisibilitytime;
		private long lastskyboxvisibilitycost;
		protected long visiblegeometryversion;

		private long lastslowculllogtime;
		private long lastslowskyboxculllogtime;

		private sealed class VisualGeometrySectorComparer : IComparer<VisualGeometry>
		{
			public static readonly VisualGeometrySectorComparer Instance = new VisualGeometrySectorComparer();

			public int Compare(VisualGeometry left, VisualGeometry right)
			{
				if(object.ReferenceEquals(left, right)) return 0;
				int result = left.Sector.Sector.FixedIndex.CompareTo(right.Sector.Sector.FixedIndex);
				if(result != 0) return result;
				return left.VertexOffset.CompareTo(right.VertexOffset);
			}
		}

		#endregion

		#region ================== Properties

		public bool ProcessGeometry { get { return processgeometry; } set { processgeometry = value; } }
		public bool ProcessThings { get { return processthings; } set { processthings = value; } }
		public VisualBlockMap BlockMap { get { return blockmap; } }
		protected bool BlockMapIsCurrent { get { return blockmapiscurrent; } }
		public Dictionary<Vertex, VisualVertexPair> VisualVertices { get { return vertices; } } //mxd
		public Dictionary<Sector, List<VisualSlope>> AllSlopeHandles { get { return allslopehandles; } }
		public Dictionary<Sector, List<VisualSlope>> SidedefSlopeHandles { get { return sidedefslopehandles; } }
		public Dictionary<Sector, List<VisualSlope>> VertexSlopeHandles { get { return vertexslopehandles; } }
		public HashSet<VisualSlope> UsedSlopeHandles { get { return usedslopehandles; } }

		public Vector3D PickingDir =>
			cursorunlocked && mouseworlddir.IsFinite() ?
				mouseworlddir :
				General.Map.VisualCamera.Target - General.Map.VisualCamera.Position;

		// Rendering
		public IRenderer3D Renderer { get { return renderer; } }
		
		#endregion

		#region ================== Constructor / Disposer

		/// <summary>
		/// Provides specialized functionality for a visual (3D) Doom Builder editing mode.
		/// </summary>
		protected VisualMode()
		{
			// Initialize
			this.renderer = General.Map.Renderer3D;
			this.blockmap = General.Map.AcquireVisualBlockMap(out this.blockmapiscurrent);
			this.blockmapchangeversion = (this.blockmapiscurrent ? General.Map.ChangeVersion : -1);
			this.allsectors = new Dictionary<Sector, VisualSector>(General.Map.Map.Sectors.Count);
			this.allthings = new Dictionary<Thing, VisualThing>(General.Map.Map.Things.Count);
			this.allslopehandles = new Dictionary<Sector, List<VisualSlope>>(General.Map.Map.Sectors.Count);
			this.sidedefslopehandles = new Dictionary<Sector, List<VisualSlope>>(General.Map.Map.Sectors.Count);
			this.vertexslopehandles = new Dictionary<Sector, List<VisualSlope>>(General.Map.Map.Sectors.Count);
			// Reserve realistic working-set sizes once. Large maps otherwise repeatedly
			// grow and copy these lists during the first few camera turns.
			int sectorcapacity = Math.Min(General.Map.Map.Sectors.Count, 1024);
			int linecapacity = Math.Min(General.Map.Map.Linedefs.Count, 4096);
			int thingcapacity = Math.Min(General.Map.Map.Things.Count, 1024);
			this.visibleblocks = new List<VisualBlockEntry>(Math.Max(64, linecapacity / 4));
			this.visiblesectors = new List<VisualSector>(Math.Max(50, sectorcapacity));
			this.visiblegeometry = new List<VisualGeometry>(Math.Max(200, linecapacity * 2));
			this.visiblethings = new List<VisualThing>(Math.Max(100, thingcapacity));
			this.candidatevisibleblocks = new List<VisualBlockEntry>(Math.Max(64, linecapacity / 4));
			this.skyboxvisibleblocks = new List<VisualBlockEntry>(Math.Max(64, linecapacity / 4));
			this.skyboxvisiblethings = new List<VisualThing>(Math.Max(100, thingcapacity));
			this.skyboxvisiblegeometry = new List<VisualGeometry>(Math.Max(200, linecapacity * 2));
			this.skyboxcandidatevisibleblocks = new List<VisualBlockEntry>(Math.Max(64, linecapacity / 4));
			this.usedslopehandles = new HashSet<VisualSlope>();
			this.visiblelines = new List<Linedef>(Math.Max(200, linecapacity));
			this.pickblocks = new List<VisualBlockEntry>(32);
			this.picklines = new HashSet<Linedef>();
			this.picksectors = new HashSet<Sector>();
			this.pickthings = new HashSet<Thing>();
			this.pickvertices = new HashSet<Vertex>();
			this.pickables = new List<IVisualPickable>(200);
			this.potentialpicks = new List<IVisualPickable>(100);
			this.sectorqueryblocks = new List<VisualBlockEntry>(16);
			this.sectorquerysectors = new List<Sector>(4);
			this.sectorquerylinedefs = new HashSet<Linedef>();
			this.cullmovementlines = new HashSet<Linedef>();
			this.blockmemberscratch = new HashSet<VisualBlockEntry>();
			this.lastsectorqueryposition = new Vector2D(double.NaN, double.NaN);
			this.haslastsectorqueryposition = false;
			this.haslastcullcamera = false;
			this.hasskyboxvisibility = false;
			this.processgeometry = true;
			this.processthings = true;
			this.mousepos = new Vector2D(float.NaN, float.NaN);
			this.mouseworlddir = new Vector3D(float.NaN, float.NaN, float.NaN);
			this.vertices = new Dictionary<Vertex, VisualVertexPair>(); //mxd
			this.pickingmode = PickingMode.Default;

			UpdateInitialCameraPosition(General.Editing.Mode);
		}

		// Geometry can be retained between 2D/Visual mode switches, but the requested
		// entry point cannot. Refresh it from the outgoing 2D mode on every switch so
		// moving across the map in 2D never reopens Visual mode at an old location.
		internal void UpdateInitialCameraPosition(EditMode sourcemode)
		{
			if(!General.Settings.GZSynchCameras || !(sourcemode is ClassicMode)) return;

			ClassicMode classicmode = (ClassicMode)sourcemode;
			if(classicmode.IsMouseInside)
				initialcameraposition = new Vector2D(classicmode.MouseMapPos.x, classicmode.MouseMapPos.y);
			else
				initialcameraposition = new Vector2D(General.Map.CRenderer2D.Viewport.Left + General.Map.CRenderer2D.Viewport.Width / 2.0f,
					General.Map.CRenderer2D.Viewport.Top + General.Map.CRenderer2D.Viewport.Height / 2.0f);
		}
		
		// Disposer
		public override void Dispose()
		{
			// Not already disposed?
			if(!isdisposed)
			{
				// Clean up
				if(allsectors != null)
				{
					foreach(KeyValuePair<Sector, VisualSector> s in allsectors)
						if(s.Value != null) s.Value.Dispose();
				}
				if(allthings != null)
				{
					foreach(KeyValuePair<Thing, VisualThing> t in allthings)
						if(t.Value != null) t.Value.Dispose();
				}

				// A late application shutdown can clear General.Map before the editing
				// manager disposes its active mode. Release the map-owned cache while the
				// map exists; otherwise dispose the detached index directly.
				if(General.Map != null)
					General.Map.ReleaseVisualBlockMap(blockmap,
						blockmapiscurrent && blockmapchangeversion == General.Map.ChangeVersion);
				else if(blockmap != null)
					blockmap.Dispose();
				visiblesectors = null;
				visiblegeometry = null;
				visibleblocks = null;
				candidatevisibleblocks = null;
				visiblethings = null;
				skyboxvisibleblocks = null;
				skyboxcandidatevisibleblocks = null;
				skyboxvisiblethings = null;
				skyboxvisiblegeometry = null;
				visiblelines = null;
				pickblocks = null;
				picklines = null;
				picksectors = null;
				pickthings = null;
				pickvertices = null;
				pickables = null;
				potentialpicks = null;
				sectorqueryblocks = null;
				sectorquerysectors = null;
				sectorquerylinedefs = null;
				cullmovementlines = null;
				blockmemberscratch = null;
				allsectors = null;
				allthings = null;
				blockmap = null;

				//mxd
				selectedVisualSectors = null;
				selectedVisualThings = null;
				
				// Done
				base.Dispose();
			}
		}

		#endregion

		#region ================== Start / Stop

		// Mode is engaged
		public override void OnEngage()
		{
			base.OnEngage();
			haslastsectorqueryposition = false;
			haslastcullcamera = false;

			// Update projection (mxd)
			General.Map.CRenderer3D.CreateProjection();
			
			// Used-texture state is maintained by map editing actions. Re-scanning every
			// sidedef and sector here made entering visual mode scale with the whole map
			// even when no texture had changed.

			// A derived mode may need the blockmap before it can call this base
			// implementation. Do not build the same all-map index twice on entry.
			if(!blockmapiscurrent) FillBlockMap();

			//mxd. Synch camera position to cursor position or center of the screen in 2d-mode
			if(General.Settings.GZSynchCameras) 
			{
				// Keep previous camera position if Control is held and camera was previously moved in Visual mode
				if(!General.Interface.CtrlState || General.Map.VisualCamera.Position.GetLengthSq() == 0)
				{
					//If initial position is inside or nearby a sector - adjust camera.z accordingly
					double posz = General.Map.VisualCamera.Position.z;
					Sector nearestsector = General.Map.Map.GetSectorByCoordinates(initialcameraposition, blockmap);

					if(nearestsector != null)
					{
						int sectorheight = nearestsector.CeilHeight - nearestsector.FloorHeight;
						if(sectorheight < 48)
							posz = nearestsector.FloorHeight + Math.Max(16, sectorheight / 2);
						else if(General.Map.VisualCamera.Position.z < nearestsector.FloorHeight + 41)
							posz = nearestsector.FloorHeight + 41; // same as in doom
						else if(General.Map.VisualCamera.Position.z > nearestsector.CeilHeight)
							posz = nearestsector.CeilHeight - 4;
					}

					General.Map.VisualCamera.Position = new Vector3D(initialcameraposition.x, initialcameraposition.y, posz);
				}
			} 
			else 
			{
				General.Map.VisualCamera.PositionAtThing();
			}

			// Start special input mode
			General.Interface.EnableProcessing();
			if(!cursorunlocked)
				General.Interface.StartExclusiveMouseInput();
		}

		// Mode is disengaged
		public override void OnDisengage()
		{
			// Visual Mode instances are cached for quick 2D/3D round-trips. Key-up
			// messages can arrive after this mode has disengaged, so never cache its
			// transient movement state or the camera will move by itself next time.
			keyforward = false;
			keybackward = false;
			keyleft = false;
			keyright = false;
			keyup = false;
			keydown = false;
			orbit = false;
			orbitPointPicked = false;

			base.OnDisengage();
			
			// Apply camera position to thing
			General.Map.VisualCamera.ApplyToThing();
			
			// Do not leave the sector on the camera
			General.Map.VisualCamera.Sector = null;

			//mxd
			selectedVisualSectors = null;
			selectedVisualThings = null;

			//mxd. Extra floors may've been edited
			General.Map.Renderer2D.UpdateExtraFloorFlag();
			
			// Stop special input mode
			General.Interface.DisableProcessing();
			General.Interface.StopExclusiveMouseInput();
		}

		#endregion

		#region ================== Events

		public override bool OnUndoBegin()
		{
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();
			return base.OnUndoBegin();
		}

		public override void OnUndoEnd()
		{
			base.OnUndoEnd();
			ResourcesReloadedPartial();
			renderer.SetCrosshairBusy(false);
		}

		public override bool OnRedoBegin()
		{
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();
			return base.OnRedoBegin();
		}

		public override void OnRedoEnd()
		{
			base.OnRedoEnd();
			ResourcesReloadedPartial();
			renderer.SetCrosshairBusy(false);
		}

		public override bool OnScriptRunBegin()
		{
			renderer.SetCrosshairBusy(true);
			return base.OnScriptRunBegin();
		}

		public override void OnScriptRunEnd()
		{
			base.OnScriptRunEnd();
			ResourcesReloadedPartial();
			renderer.SetCrosshairBusy(false);
		}

		public override void OnReloadResources()
		{
			base.OnReloadResources();
			ResourcesReloaded();
		}

		//mxd
		public override bool OnMapTestBegin(bool testFromCurrentPosition) 
		{
			if(testFromCurrentPosition) 
			{
				//find Single Player Start. Should have Type 1 in all games
				Thing start = null;
				foreach(Thing t in General.Map.Map.Things) 
				{
					if(t.Type == 1) 
					{
						//store thing and position
						start = t;
						break;
					}
				}

				if(start == null) 
				{
					General.MainWindow.DisplayStatus(StatusType.Warning, "Can't test from current position: no Player 1 start found!");
					return false;
				}

				//now check if camera is located inside a sector
				Vector3D camPos = General.Map.VisualCamera.Position;
				Sector s = General.Map.Map.GetSectorByCoordinates(new Vector2D(camPos.x, camPos.y), blockmap);

				if(s == null) 
				{
					General.MainWindow.DisplayStatus(StatusType.Warning, "Can't test from current position: cursor is not inside sector!");
					return false;
				}

				//41 = player's height in Doom. Is that so in all other games as well?
				if(s.CeilHeight - s.FloorHeight < 48) 
				{
					General.MainWindow.DisplayStatus(StatusType.Warning, "Can't test from current position: sector is too low!");
					return false;
				}

				//check camera Z
				double pz = camPos.z - s.FloorHeight;
				int ceilRel = s.CeilHeight - s.FloorHeight - 41; //relative ceiling height
				if(pz > ceilRel) pz = ceilRel; //above ceiling?
				else if(pz < 0) pz = 0; //below floor?

				//store initial position
				playerStart = start;
				playerStartPosition = start.Position;
				playerStartAngle = start.Angle;

				//everything should be valid, let's move player start here
				start.Move(new Vector3D(camPos.x, camPos.y, pz));
				start.Rotate(General.Map.VisualCamera.AngleXY - Angle2D.PI);// (float)Math.PI);
			}
			return true;
		}

		//mxd
		public override void OnMapTestEnd(bool testFromCurrentPosition) 
		{
			if(testFromCurrentPosition) 
			{
				//restore position
				playerStart.Move(playerStartPosition);
				playerStart.Rotate(playerStartAngle);
				playerStart = null;
			}
		}
		
		#endregion
		
		#region ================== Input

		// Mouse leaves the display
		public override void OnMouseLeave(EventArgs e)
		{
			// Mouse is outside the display
			mousepos = new Vector2D(float.NaN, float.NaN);
			mouseworlddir = new Vector3D(float.NaN, float.NaN, float.NaN);

			// Let the base class know
			base.OnMouseLeave(e);
		}

		// Mouse moved inside the display
		public override void OnMouseMove(MouseEventArgs e)
		{
			// Record last position
			mousepos = new Vector2D(e.X, e.Y);
			mouseworlddir = renderer.DisplayToWorld(mousepos);

			// Let the base class know
			base.OnMouseMove(e);
		}

		// Mouse input
		public override void OnMouseInput(Vector2D delta)
		{
			base.OnMouseInput(delta);

			if (orbit)
			{
				Vector3D start = General.Map.VisualCamera.Position;
				
				if (!orbitPointPicked)
				{
					Vector3D hitPosition = GetHitPosition();
					orbitPoint = hitPosition;
					
					if (!hitPosition.IsFinite())
					{
						return;
					}
					
					orbitPointPicked = true;

					Vector3D pickDelta = start - orbitPoint;
					orbitRadius = pickDelta.GetLength();
					orbitXY = pickDelta.GetNormal().GetAngleXY();
					orbitZ = -pickDelta.GetNormal().GetAngleZ();
				}
				
				// Change camera angles with the mouse changes
				orbitXY -= delta.x * VisualCamera.ANGLE_FROM_MOUSE;
				if(General.Settings.InvertYAxis)
					orbitZ -= delta.y * VisualCamera.ANGLE_FROM_MOUSE;
				else
					orbitZ += delta.y * VisualCamera.ANGLE_FROM_MOUSE;
				
				orbitXY = Angle2D.Normalized(orbitXY);
				orbitZ = Angle2D.Normalized(orbitZ);

				if (orbitZ < VisualCamera.MAX_ANGLEZ_LOW) orbitZ = VisualCamera.MAX_ANGLEZ_LOW;
				if (orbitZ > VisualCamera.MAX_ANGLEZ_HIGH) orbitZ = VisualCamera.MAX_ANGLEZ_HIGH;
				
				Vector3D orbitDelta = Vector3D.FromAngleXYZ(orbitXY, orbitZ);

				Vector3D newPosition = orbitPoint - orbitDelta * orbitRadius;
				Vector3D positionUpdate = newPosition - General.Map.VisualCamera.Position;

				General.Map.VisualCamera.Position += positionUpdate * General.Map.VisualCamera.MoveMultiplier;
				start = General.Map.VisualCamera.Position;
				
				// Recalculate angles to ensure we're always looking at the orbit point
				Vector3D updatedDelta = start - orbitPoint;
				orbitXY = updatedDelta.GetNormal().GetAngleXY();
				orbitZ = -updatedDelta.GetNormal().GetAngleZ();
				
				General.Map.VisualCamera.AngleZ = orbitZ;
				General.Map.VisualCamera.AngleXY = orbitXY;

				General.Map.VisualCamera.ProcessMouseInput(new Vector2D());
			}
			else
			{
				General.Map.VisualCamera.ProcessMouseInput(delta);	
			}
		}

		[BeginAction("moveforward", BaseAction = true)]
		public virtual void BeginMoveForward()
		{
			keyforward = true;
		}

		[EndAction("moveforward", BaseAction = true)]
		public virtual void EndMoveForward()
		{
			keyforward = false;
		}

		[BeginAction("movebackward", BaseAction = true)]
		public virtual void BeginMoveBackward()
		{
			keybackward = true;
		}

		[EndAction("movebackward", BaseAction = true)]
		public virtual void EndMoveBackward()
		{
			keybackward = false;
		}

		[BeginAction("moveleft", BaseAction = true)]
		public virtual void BeginMoveLeft()
		{
			keyleft = true;
		}

		[EndAction("moveleft", BaseAction = true)]
		public virtual void EndMoveLeft()
		{
			keyleft = false;
		}

		[BeginAction("moveright", BaseAction = true)]
		public virtual void BeginMoveRight()
		{
			keyright = true;
		}

		[EndAction("moveright", BaseAction = true)]
		public virtual void EndMoveRight()
		{
			keyright = false;
		}
		
		[BeginAction("orbit", BaseAction = true)]
		public virtual void BeginOrbit()
		{
			orbit = true;
		}

		[EndAction("orbit", BaseAction = true)]
		public virtual void EndOrbit()
		{
			orbit = false;
			orbitPointPicked = false;
		}

		[BeginAction("unlockcursor", BaseAction = true)]
		public virtual void BeginUnlockCursor()
		{
			cursorunlocked = !cursorunlocked;

			if(cursorunlocked)
			{
				General.Interface.StopExclusiveMouseInput();
			}
			else
			{
				General.Interface.StartExclusiveMouseInput();
			}
		}

		[BeginAction("moveup", BaseAction = true)]
		public virtual void BeginMoveUp()
		{
			keyup = true;
		}

		[EndAction("moveup", BaseAction = true)]
		public virtual void EndMoveUp()
		{
			keyup = false;
		}

		[BeginAction("movedown", BaseAction = true)]
		public virtual void BeginMoveDown()
		{
			keydown = true;
		}

		[EndAction("movedown", BaseAction = true)]
		public virtual void EndMoveDown()
		{
			keydown = false;
		}
		
		[BeginAction("movecameratocursor", BaseAction = true)]
		protected virtual void MoveCameraToCursor() 
		{
			if (orbit)
			{
				orbitRadius = MOVE_CAMERA_DISTANCE;
			}
			else
			{
				Vector3D hitPosition = GetHitPosition();
				if (!hitPosition.IsFinite())
				{
					return;
				}

				Vector3D start = General.Map.VisualCamera.Position;
				Vector3D delta = start - hitPosition;
				General.Map.VisualCamera.Position = hitPosition + delta.GetFixedLength(MOVE_CAMERA_DISTANCE);
			}
		}


		//mxd
		[BeginAction("movethingleft", BaseAction = true)]
		protected void MoveSelectedThingsLeft() 
		{
			MoveSelectedThings(new Vector2D(0f, -General.Map.Grid.GridSizeF), false);
		}
		//mxd
		[BeginAction("movethingright", BaseAction = true)]
		protected void MoveSelectedThingsRight() 
		{
			MoveSelectedThings(new Vector2D(0f, General.Map.Grid.GridSizeF), false);
		}
		//mxd
		[BeginAction("movethingfwd", BaseAction = true)]
		protected void MoveSelectedThingsForward() 
		{
			MoveSelectedThings(new Vector2D(-General.Map.Grid.GridSizeF, 0f), false);
		}
		//mxd
		[BeginAction("movethingback", BaseAction = true)]
		protected void MoveSelectedThingsBackward() 
		{
			MoveSelectedThings(new Vector2D(General.Map.Grid.GridSizeF, 0f), false);
		}

		//mxd
		[BeginAction("placethingatcursor", BaseAction = true)]
		protected void PlaceThingAtCursor() 
		{
			Vector2D hitpos = GetHitPosition();
			if(!hitpos.IsFinite()) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Cannot place Thing here");
				return;
			}

			MoveSelectedThings(new Vector2D(Math.Round(hitpos.x), Math.Round(hitpos.y)), true);
		}

		//mxd. 
		public Vector3D GetHitPosition() 
		{
			Vector3D start = General.Map.VisualCamera.Position;
			Vector3D delta = PickingDir;
			delta = delta.GetFixedLength(General.Settings.ViewDistance * 0.98f);
			VisualPickResult target = PickObject(start, start + delta);

			if(target.picked == null) return new Vector3D(double.NaN, double.NaN, double.NaN);

			// Now find where exactly did we hit
			VisualGeometry vg = target.picked as VisualGeometry;
			if(vg != null) return GetIntersection(start, start + delta, vg.BoundingBox[0], new Vector3D(vg.Vertices[0].nx, vg.Vertices[0].ny, vg.Vertices[0].nz));


			VisualThing vt = target.picked as VisualThing;
			if (vt != null)
			{
				Vector3D normal = start - vt.CenterV3D;
				normal.z = 0;
				normal = normal.GetNormal();
				return GetIntersection(start, start + delta, vt.CenterV3D, normal);
			}

			return new Vector3D(double.NaN, double.NaN, double.NaN);
		}

		//mxd. This checks intersection between line and plane 
		private static Vector3D GetIntersection(Vector3D start, Vector3D end, Vector3D planeCenter, Vector3D planeNormal) 
		{
			Vector3D delta = new Vector3D(planeCenter.x - start.x, planeCenter.y - start.y, planeCenter.z - start.z);
			return start + Vector3D.DotProduct(planeNormal, delta) / Vector3D.DotProduct(planeNormal, end - start) * (end - start);
		}

		//mxd. Should move selected things in specified direction
		protected virtual void MoveSelectedThings(Vector2D direction, bool absolutePosition) { }

        #endregion

        #region ================== Visibility Culling

		int lastProcessed = 0;

		// Near-first traversal order can change as the camera moves across a quadtree
		// midpoint even when the frustum still contains exactly the same blocks. Compare
		// membership, since the final geometry is sorted independently of block order.
		private bool SameBlockMembership(List<VisualBlockEntry> left, List<VisualBlockEntry> right)
		{
			if(left.Count != right.Count) return false;
			blockmemberscratch.Clear();
			for(int i = 0; i < left.Count; i++) blockmemberscratch.Add(left[i]);
			for(int i = 0; i < right.Count; i++)
				if(!blockmemberscratch.Remove(right[i])) return false;
			return blockmemberscratch.Count == 0;
		}

		// Sidedef visibility is chosen from the camera's side of each visible line.
		// Test every line in the retained block set, not only finite line segments
		// touched by the movement path. Moving around a linedef endpoint can cross its
		// infinite side plane without crossing the segment itself; missing that change
		// reused the old face and made the newly visible wall disappear temporarily.
		private bool MovementChangedLineSide(Vector2D from, Vector2D to, List<VisualBlockEntry> blocks)
		{
			if(from == to) return false;
			cullmovementlines.Clear();
			foreach(VisualBlockEntry block in blocks)
			{
				foreach(Linedef line in block.Lines)
				{
					if(!cullmovementlines.Add(line)) continue;
					double first = line.SideOfLine(from);
					double second = line.SideOfLine(to);
					int firstside = (first < -0.000001 ? -1 : (first > 0.000001 ? 1 : 0));
					int secondside = (second < -0.000001 ? -1 : (second > 0.000001 ? 1 : 0));
					if(firstside != secondside) return true;
				}
			}
			return false;
		}

		// This performs visibility culling. Normal processing may reuse the completed
		// result while only the camera direction changes and the frustum still covers
		// exactly the same spatial blocks. Forced/editor refreshes always rebuild it.
		protected void DoCulling(bool allowunchangedblockreuse = false)
		{
			long cullstart = Clock.CurrentTime;
			Vector2D campos2d = General.Map.VisualCamera.Position;

			// Query into a second retained list so the prior result remains available for
			// comparison. This is particularly effective while looking around in one spot:
			// the quadtree query is cheap, while walking thousands of lines, rebuilding
			// geometry lists and sorting them was the expensive part of the cull.
			blockmap.GetFrustumRange(renderer.Frustum2D, campos2d, candidatevisibleblocks);
			long blockquerydone = Clock.CurrentTime;
			bool positionunchanged = haslastcullcamera
				&& lastcullcameraposition.x == campos2d.x
				&& lastcullcameraposition.y == campos2d.y;
			bool sameblockmembership = allowunchangedblockreuse && haslastcullcamera
				&& SameBlockMembership(candidatevisibleblocks, visibleblocks);
			if(sameblockmembership && (positionunchanged
				|| !MovementChangedLineSide(lastcullcameraposition, campos2d, visibleblocks)))
			{
				lastcullcost = blockquerydone - cullstart;
				return;
			}

			List<VisualBlockEntry> previousblocks = visibleblocks;
			visibleblocks = candidatevisibleblocks;
			candidatevisibleblocks = previousblocks;
			candidatevisibleblocks.Clear();

			if(!allowunchangedblockreuse) InvalidateSkyboxVisibility();
			lastProcessed++;
			visiblelines.Clear();
			// Preserve collection capacity between frames. Replacing these lists on
			// every tick was a major source of garbage collections on large maps.
			visiblesectors.Clear();
			visiblegeometry.Clear();
			visiblethings.Clear();

			// Find the camera sector before traversing the frustum so its floor and
			// ceiling are the first resources built. Looking around does not change the
			// camera sector, so avoid repeating this point-in-sector query on mouse frames.
			bool positionchanged = !haslastsectorqueryposition
				|| lastsectorqueryposition.x != campos2d.x
				|| lastsectorqueryposition.y != campos2d.y;
			Sector camsector = General.Map.VisualCamera.Sector;
			if(positionchanged)
			{
				lastsectorqueryposition = campos2d;
				haslastsectorqueryposition = true;
				camsector = blockmap.GetSectorAt(campos2d, sectorqueryblocks, sectorquerysectors, sectorquerylinedefs);
				if(camsector != null) General.Map.VisualCamera.Sector = camsector;
			}
			if(camsector != null && !allsectors.ContainsKey(camsector)) CreateVisualSector(camsector);

			// Fill collections with geometry and things
			foreach(VisualBlockEntry block in visibleblocks)
			{
				if(processgeometry)
				{
					// Lines
					foreach(Linedef ld in block.Lines)
					{
						// Line not already processed?
						if (ld.LastProcessed != lastProcessed)
						{
                            // Add line if not added yet
                            ld.LastProcessed = lastProcessed;
                            visiblelines.Add(ld);

							// Which side of the line is the camera on?
							if(ld.SideOfLine(campos2d) < 0)
							{
								// Do front of line
								if(ld.Front != null) ProcessSidedefCulling(ld.Front);
							}
							else
							{
								// Do back of line
								if(ld.Back != null) ProcessSidedefCulling(ld.Back);
							}
						}
					}
				}

				if(processthings)
				{
					// Things
					foreach(Thing t in block.Things)
					{
                        if (t.LastProcessed == lastProcessed) continue;

                        t.LastProcessed = lastProcessed;

						// Not filtered out?
						if(!General.Map.ThingsFilter.IsThingVisible(t)) continue;

						VisualThing vt;
						if(!allthings.TryGetValue(t, out vt))
						{
							vt = CreateVisualThing(t);
							allthings[t] = vt;
						}

						if(vt != null)
						{
							visiblethings.Add(vt);
						}
					}
				}
			}

			if(processgeometry && positionchanged && camsector == null)
			{
				// When geometry is visible, use its nearest line to determine the side
				// of the map the camera is on. If no lines are in view the camera is
				// outside the map; never fall back to an O(all linedefs) scan.
				Linedef nearestvisibleline = MapSet.NearestLinedef(visiblelines, campos2d);
				General.Map.VisualCamera.Sector = (nearestvisibleline != null
					? GetCameraSectorFromLinedef(nearestvisibleline) : null);
			}
			else if(processgeometry && camsector != null && visiblelines.Count == 0)
			{
				// Large sectors can contain the camera while every boundary lies outside
				// the projected frustum. Add their inward-facing boundaries explicitly so
				// the floor, ceiling and editable walls never disappear in that case.
				foreach(Sidedef sd in camsector.Sidedefs)
				{
					double side = sd.Line.SideOfLine(campos2d);
					if(((side < 0.0) && sd.IsFront) || ((side > 0.0) && !sd.IsFront))
						ProcessSidedefCulling(sd);
				}
			}

			long collectiondone = Clock.CurrentTime;

			// The renderer groups this stable list by texture on every rendered frame.
			// Sorting it once per cull preserves sector order in every texture subset,
			// eliminating repeated per-texture sorts at high frame caps.
			if(visiblegeometry.Count > 1)
				visiblegeometry.Sort(VisualGeometrySectorComparer.Instance);
			visiblegeometryversion++;

			long culldone = Clock.CurrentTime;
			long culltime = culldone - cullstart;
			lastcullcost = culltime;
			if(culltime >= 25 && Clock.CurrentTime - lastslowculllogtime >= 1000)
			{
				General.WriteLogLine("Slow Visual Mode cull: " + culltime + " ms for " + visibleblocks.Count
					+ " blocks, " + visiblelines.Count + " lines and " + visiblegeometry.Count + " geometry items"
					+ " (block query " + (blockquerydone - cullstart) + " ms, collect/build "
					+ (collectiondone - blockquerydone) + " ms, sort " + (culldone - collectiondone) + " ms)");
				lastslowculllogtime = Clock.CurrentTime;
			}
		}

		// This finds and adds visible sectors
		private void ProcessSidedefCulling(Sidedef sd)
		{
            // Do nothing if we already added it.
			if (sd.LastProcessed == lastProcessed)
                return;

            sd.LastProcessed = lastProcessed;

			VisualSector vs;

			// Find the visual sector and create it before this frame is rendered.
			// This restores the original UZB guarantee that visible geometry is also
			// immediately available to the picker.
			if(!allsectors.TryGetValue(sd.Sector, out vs))
				vs = CreateVisualSector(sd.Sector);
			
			if(vs != null)
			{
                if (sd.Sector.LastProcessed != lastProcessed)
                {
                    sd.Sector.LastProcessed = lastProcessed;
                    visiblesectors.Add(vs);
                    visiblegeometry.AddRange(vs.FixedGeometry);
                }
				
				if(!vs.SidedefGeometryReady(sd)) vs.PrepareSidedefGeometry(sd);

				visiblegeometry.AddRange(vs.GetSidedefGeometry(sd));
			}
		}

		// This returns the camera sector from linedef
		private static Sector GetCameraSectorFromLinedef(Linedef ld)
		{
			if(ld.SideOfLine(General.Map.VisualCamera.Position) < 0)
			{
				return (ld.Front != null ? ld.Front.Sector : null);
			}

			return (ld.Back != null ? ld.Back.Sector : null);
		}
		
		#endregion

		#region ================== Object Picking

		// This picks an object from the scene
		public VisualPickResult PickObject(Vector3D from, Vector3D to)
		{
			VisualPickResult result = new VisualPickResult();
			Line2D ray2d = new Line2D(from, to);
			Vector3D delta = to - from;
			
			// Setup no result
			result.picked = null;
			result.hitpos = new Vector3D();
			result.u_ray = 1.0f;
			
			// Reuse the picking collections. Picking is performed continuously in
			// visual mode, so per-pick dictionaries and lists quickly become GC churn.
			blockmap.GetLineBlocks(from, to, pickblocks);
			picklines.Clear();
			picksectors.Clear();
			pickthings.Clear();
			pickvertices.Clear();
			pickables.Clear();
			potentialpicks.Clear();

			// Add geometry from the camera sector
			VisualSector vs;
			if((General.Map.VisualCamera.Sector != null) &&
			   allsectors.TryGetValue(General.Map.VisualCamera.Sector, out vs))
			{
				picksectors.Add(General.Map.VisualCamera.Sector);
				foreach(VisualGeometry g in vs.FixedGeometry) pickables.Add(g);

				// Add slope handles
				if (General.Map.UDMF)
				{
					if (pickingmode == PickingMode.SidedefSlopeHandles && sidedefslopehandles.ContainsKey(General.Map.VisualCamera.Sector))
						pickables.AddRange(sidedefslopehandles[General.Map.VisualCamera.Sector]);
					else if (pickingmode == PickingMode.VertexSlopeHandles && vertexslopehandles.ContainsKey(General.Map.VisualCamera.Sector))
						pickables.AddRange(vertexslopehandles[General.Map.VisualCamera.Sector]);
				}
			}
			
			// Go for all lines to see which ones we intersect
			// We will collect geometry from the sectors and sidedefs
			foreach(VisualBlockEntry b in pickblocks)
			{
				foreach(Linedef ld in b.Lines)
				{
					// Make sure we don't test a line twice
					if(picklines.Add(ld))
					{
						// Intersecting?
						double u;
						if(ld.Line.GetIntersection(ray2d, out u))
						{
							// Check on which side we are
							double side = ld.SideOfLine(ray2d.v1);
							
							// Calculate intersection point
							Vector3D intersect = from + delta * u;
							
							// We must add the sectors of both sides of the line
							// If we wouldn't, then aiming at a sector that is just within range
							// could result in an incorrect hit (because the far line of the
							// sector may not be included in this loop)
							if(ld.Front != null)
							{
								// Find the visualsector
								if(allsectors.TryGetValue(ld.Front.Sector, out vs))
								{
									// Add sector if not already added
									if(picksectors.Add(ld.Front.Sector))
									{
										foreach (VisualGeometry g in vs.FixedGeometry)
										{
											// Must have content
											if (g.Triangles > 0)
												pickables.Add(g);
										}

										// Add slope handles
										if (General.Map.UDMF)
										{
											if (pickingmode == PickingMode.SidedefSlopeHandles && sidedefslopehandles.ContainsKey(ld.Front.Sector))
												pickables.AddRange(sidedefslopehandles[ld.Front.Sector]);
											else if (pickingmode == PickingMode.VertexSlopeHandles && vertexslopehandles.ContainsKey(ld.Front.Sector))
												pickables.AddRange(vertexslopehandles[ld.Front.Sector]);
										}
									}
									
									// Add sidedef if on the front side
									if(side < 0.0f)
									{
										List<VisualGeometry> sidedefgeo = vs.GetSidedefGeometry(ld.Front);
										foreach(VisualGeometry g in sidedefgeo)
										{
											// Must have content
											if(g.Triangles > 0)
											{
												g.SetPickResults(intersect, u);
												pickables.Add(g);
											}
										}
									}
								}
							}
							
							// Add back side also
							if(ld.Back != null)
							{
								// Find the visualsector
								if(allsectors.TryGetValue(ld.Back.Sector, out vs))
								{
									// Add sector if not already added
									if(picksectors.Add(ld.Back.Sector))
									{
										foreach (VisualGeometry g in vs.FixedGeometry)
										{
											// Must have content
											if (g.Triangles > 0)
												pickables.Add(g);
										}

										// Add slope handles
										if (General.Map.UDMF)
										{
											if (pickingmode == PickingMode.SidedefSlopeHandles && sidedefslopehandles.ContainsKey(ld.Back.Sector))
												pickables.AddRange(sidedefslopehandles[ld.Back.Sector]);
											else if (pickingmode == PickingMode.VertexSlopeHandles && vertexslopehandles.ContainsKey(ld.Back.Sector))
												pickables.AddRange(vertexslopehandles[ld.Back.Sector]);
										}
									}

									// Add sidedef if on the front side
									if(side > 0.0f)
									{
										List<VisualGeometry> sidedefgeo = vs.GetSidedefGeometry(ld.Back);
										foreach(VisualGeometry g in sidedefgeo)
										{
											// Must have content
											if(g.Triangles > 0)
											{
												g.SetPickResults(intersect, u);
												pickables.Add(g);
											}
										}
									}
								}
							}
						}
					}
				}

				// Only things in blocks touched by the picking ray can be hit. Testing all
				// things in the camera frustum caused noticeable spikes on populated maps.
				foreach(Thing t in b.Things)
				{
					VisualThing vt;
					if(pickthings.Add(t) && allthings.TryGetValue(t, out vt) && vt != null)
						pickables.Add(vt);
				}
			}

			// Visual vertices are attached to map lines. Restrict them to the blocks
			// touched by the picking ray instead of testing every visual vertex in the
			// map on each pick interval.
			if (General.Map.UDMF && General.Settings.GZShowVisualVertices)
			{
				foreach(Linedef ld in picklines)
				{
					VisualVertexPair pair;
					if(pickvertices.Add(ld.Start) && vertices.TryGetValue(ld.Start, out pair))
						pickables.AddRange(pair.Vertices);
					if(pickvertices.Add(ld.End) && vertices.TryGetValue(ld.End, out pair))
						pickables.AddRange(pair.Vertices);
				}
			}
			
			// Now we have a list of potential geometry that lies along the trace line.
			// We still don't know what geometry actually hits, but we ruled out that which doesn't get even close.
			// This is still too much for accurate intersection testing, so we do a fast reject pass first.
			Vector3D direction = to - from;
			direction = direction.GetNormal();
			foreach(IVisualPickable p in pickables)
			{
				if(p.PickFastReject(from, to, direction)) potentialpicks.Add(p);
			}
			
			// Now we do an accurate intersection test for all resulting geometry
			// We keep only the closest hit!
			foreach(IVisualPickable p in potentialpicks)
			{
				double u = result.u_ray;
				if(p.PickAccurate(from, to, direction, ref u))
				{
					// Closer than the previous find? A regular middle texture and a 3D-floor
					// side can occupy exactly the same plane. In that tie, prefer the 3D-floor
					// surface, matching its later draw order and allowing its control-side
					// texture offsets/alignment to be edited instead of silently selecting the
					// normal middle texture beneath it.
					VisualGeometry candidategeometry = p as VisualGeometry;
					VisualGeometry currentgeometry = result.picked as VisualGeometry;
					bool prefer3dfloor = candidategeometry != null && currentgeometry != null
						&& candidategeometry.GeometryType == VisualGeometryType.WALL_MIDDLE_3D
						&& currentgeometry.GeometryType == VisualGeometryType.WALL_MIDDLE
						&& Math.Abs(u - result.u_ray) <= 0.0000001;
					if((u > 0.0f) && ((u < result.u_ray) || prefer3dfloor))
					{
						result.u_ray = u;
						result.picked = p;
					}
				}
			}
			
			// Setup final result
			result.hitpos = from + delta * result.u_ray;

			// If picking mode is for slope handles only return slope handles. We have to do it this
			// way because otherwise it's possible to pick slope handles through other geometry
			if (pickingmode != PickingMode.Default && !(result.picked is VisualSlope))
				result.picked = null;

			// Done
			return result;
		}
		
		#endregion

		#region ================== Processing
		
		/// <summary>
		/// This disposes all resources. Needed geometry will be rebuild automatically.
		/// </summary>
		protected virtual void ResourcesReloaded()
		{
			// Dispose
			foreach(KeyValuePair<Sector, VisualSector> vs in allsectors)
				if(vs.Value != null) vs.Value.Dispose();
				
			foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
				if(vt.Value != null) vt.Value.Dispose();
				
			// Clear collections
			allsectors.Clear();
			allthings.Clear();
			visiblesectors.Clear();
			visibleblocks.Clear();
			visiblegeometry.Clear();
			visiblethings.Clear();
			vertices.Clear(); //mxd
			
			// Make new blockmap
			FillBlockMap();

			// Specialized modes may cache map-format and effect information which must
			// be current before DoCulling creates replacement sectors and things.
			PrepareReloadResources();

			// Visibility culling (this re-creates the needed resources)
			DoCulling();
		}

		/// <summary>
		/// Gives specialized visual modes a chance to rebuild dependency data after
		/// a full resource reload and before replacement geometry is created.
		/// </summary>
		protected virtual void PrepareReloadResources() { }

		/// <summary>
		/// Gives specialized visual modes a chance to extend the changed-element marks
		/// before old visual resources are retained or disposed.
		/// </summary>
		protected virtual void ExpandPartialReloadMarks() { }

		/// <summary>
		/// Gives specialized visual modes a chance to rebuild dependency data after
		/// the spatial index is current, but before replacement geometry is culled.
		/// </summary>
		protected virtual void PreparePartialReloadResources() { }

		/// <summary>
		/// Gives specialized visual modes a chance to keep a changed sector's current
		/// visual object alive while it prepares replacement geometry incrementally.
		/// </summary>
		protected virtual bool RetainPartialReloadSector(Sector sector, VisualSector visualsector) { return false; }

		/// <summary>
		/// Gives specialized visual modes a chance to keep a changed thing's current
		/// visual object alive while it prepares the replacement incrementally.
		/// </summary>
		protected virtual bool RetainPartialReloadThing(Thing thing, VisualThing visualthing) { return false; }

		/// <summary>
		/// This disposes orphaned resources and resources on changed geometry.
		/// This usually happens when geometry is changed by undo, redo, cut or paste actions
		/// and uses the marks to check what needs to be reloaded.
		/// </summary>
		protected virtual void ResourcesReloadedPartial()
		{
			Dictionary<Sector, VisualSector> newsectors = new Dictionary<Sector,VisualSector>(allsectors.Count);
			
			// Neighbour sectors must be updated as well
			foreach(Sector s in General.Map.Map.Sectors)
			{
				if(s.Marked)
				{
					foreach(Sidedef sd in s.Sidedefs)
						if(sd.Other != null) sd.Other.Marked = true;
				}
			}
			
			// Go for all sidedefs to mark sectors that need updating
			foreach(Sidedef sd in General.Map.Map.Sidedefs)
				if(sd.Marked) sd.Sector.Marked = true;
			
			// Go for all vertices to mark linedefs that need updating
			foreach(Vertex v in General.Map.Map.Vertices)
			{
				if(v.Marked)
				{
					foreach(Linedef ld in v.Linedefs)
						ld.Marked = true;
				}
			}
			
			// Go for all linedefs to mark sectors that need updating
			foreach(Linedef ld in General.Map.Map.Linedefs)
			{
				if(ld.Marked)
				{
					if(ld.Front != null) ld.Front.Sector.Marked = true;
					if(ld.Back != null) ld.Back.Sector.Marked = true;
				}
			}

			// Specialized modes can extend the marked set with sectors which depend on
			// changed control sectors, things or vertices. This must happen before the
			// old visual resources are retained or disposed.
			ExpandPartialReloadMarks();

			// Dispose invalid sources. Specialized visual modes may retain a marked
			// resource until its replacement is ready, preventing a visible hole between
			// the partial reload and the next frame-budgeted rebuild.
			foreach(KeyValuePair<Sector, VisualSector> vs in allsectors)
			{
				if(vs.Value != null)
				{
					if(vs.Key.IsDisposed)
						vs.Value.Dispose();
					else if(vs.Key.Marked)
					{
						if(RetainPartialReloadSector(vs.Key, vs.Value))
							newsectors.Add(vs.Key, vs.Value);
						else
							vs.Value.Dispose();
					}
					else
						newsectors.Add(vs.Key, vs.Value);
				}
			}
			
			// Preserve unaffected things. Rebuilding every visible sprite/model after a
			// one-sector edit caused a second burst of decoding and GPU uploads.
			Dictionary<Thing, VisualThing> newthings = new Dictionary<Thing, VisualThing>(allthings.Count);
			foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
			{
				bool invalid = vt.Key == null || vt.Key.IsDisposed;
				bool affected = invalid || General.Map.UndoRedo.PopulationChanged || vt.Key.Marked
					|| (vt.Key.Sector != null && vt.Key.Sector.Marked);
				if(invalid)
				{
					if(vt.Value != null) vt.Value.Dispose();
				}
				else if(affected)
				{
					if(vt.Value != null && RetainPartialReloadThing(vt.Key, vt.Value))
						newthings.Add(vt.Key, vt.Value);
					else if(vt.Value != null)
						vt.Value.Dispose();
				}
				else
				{
					newthings.Add(vt.Key, vt.Value);
				}
			}
			// Apply new lists
			allsectors = newsectors;
			allthings = newthings;
			
			// Clear visibility collections
			visiblesectors.Clear();
			visibleblocks.Clear();
			visiblegeometry.Clear();
			visiblethings.Clear();
			
			// Texture, brightness and thing-property changes do not invalidate the
			// spatial index. Rebuild it only for actual geometry/population changes.
			if(General.Map.UndoRedo.GeometryChanged || General.Map.UndoRedo.PopulationChanged)
				FillBlockMap();

			// SRB2/GZDoom effects depend on the up-to-date blockmap. Rebuild those
			// relationships before DoCulling queues any replacement sector geometry.
			PreparePartialReloadResources();

			// Visibility culling (this re-creates the needed resources)
			DoCulling();
		}
		
		/// <summary>
		/// Implement this to create an instance of your VisualSector implementation.
		/// </summary>
		protected abstract VisualSector CreateVisualSector(Sector s);

		/// <summary>
		/// Implement this to create an instance of your VisualThing implementation.
		/// </summary>
		protected abstract VisualThing CreateVisualThing(Thing t);
		
		/// <summary>
		/// This returns the VisualSector for the given Sector.
		/// </summary>
		public VisualSector GetVisualSector(Sector s) 
		{
			if(!allsectors.ContainsKey(s)) return CreateVisualSector(s); //mxd
			return allsectors[s]; 
		}
		
		/// <summary>
		/// This returns the VisualThing for the given Thing.
		/// </summary>
		public VisualThing GetVisualThing(Thing t) { return allthings[t]; }

		//mxd
		public List<VisualThing> GetSelectedVisualThings(bool refreshSelection) 
		{
			if(refreshSelection || selectedVisualThings == null) 
			{
				selectedVisualThings = new List<VisualThing>();
				foreach(KeyValuePair<Thing, VisualThing> group in allthings) 
				{
					if(group.Value != null && group.Value.Selected)
						selectedVisualThings.Add(group.Value);
				}

				//if nothing is selected - try to get thing from highlighted object
				if(selectedVisualThings.Count == 0) 
				{
					Vector3D start = General.Map.VisualCamera.Position;
					Vector3D delta = PickingDir;
					delta = delta.GetFixedLength(General.Settings.ViewDistance * 0.98f);
					VisualPickResult target = PickObject(start, start + delta);

					//not appropriate way to do this, but...
					if(target.picked is VisualThing)
						selectedVisualThings.Add((VisualThing)target.picked);
				}
			}

			return selectedVisualThings;
		}

		/// <summary>
		/// mxd. This returns list of selected sectors based on surfaces selected in visual mode
		/// </summary>
		public List<VisualSector> GetSelectedVisualSectors(bool refreshSelection) 
		{
			if(refreshSelection || selectedVisualSectors == null) 
			{
				selectedVisualSectors = new List<VisualSector>();
				foreach(KeyValuePair<Sector, VisualSector> group in allsectors) 
				{
					foreach(VisualGeometry vg in group.Value.AllGeometry) 
					{
						if(vg.Selected) 
						{
							selectedVisualSectors.Add(group.Value);
							break;
						}
					}
				}

				//if nothing is selected - try to get sector from hilighted object
				if(selectedVisualSectors.Count == 0) 
				{
					VisualGeometry vg = GetHilightedSurface();
					if(vg != null) selectedVisualSectors.Add(vg.Sector);
				}
			}
			return selectedVisualSectors;
		}

		/// <summary>
		/// mxd. This returns list of surfaces selected in visual mode
		/// </summary>
		public List<VisualGeometry> GetSelectedSurfaces() 
		{
			List<VisualGeometry> selectedSurfaces = new List<VisualGeometry>();
			foreach(KeyValuePair<Sector, VisualSector> group in allsectors) 
			{
				foreach(VisualGeometry vg in group.Value.AllGeometry) 
				{
					if(vg.Selected) selectedSurfaces.Add(vg);
				}
			}

			//if nothing is selected - try to get hilighted surface
			if(selectedSurfaces.Count == 0) 
			{
				VisualGeometry vg = GetHilightedSurface();
				if(vg != null) selectedSurfaces.Add(vg);
			}
			return selectedSurfaces;
		}

		//mxd
		private VisualGeometry GetHilightedSurface() 
		{
			Vector3D start = General.Map.VisualCamera.Position;
			Vector3D delta = PickingDir;
			delta = delta.GetFixedLength(General.Settings.ViewDistance * 0.98f);
			VisualPickResult target = PickObject(start, start + delta);

			if(target.picked is VisualGeometry) 
			{
				VisualGeometry vg = (VisualGeometry)target.picked;
				if(vg.Sector != null) return vg;
			}
			return null;
		}

		/// <summary>
		/// Returns True when a VisualSector has been created for the specified Sector.
		/// </summary>
		public bool VisualSectorExists(Sector s) { return allsectors.ContainsKey(s) && (allsectors[s] != null); }

		/// <summary>
		/// Returns True when a VisualThing has been created for the specified Thing.
		/// </summary>
		public bool VisualThingExists(Thing t) { return allthings.ContainsKey(t) && (allthings[t] != null); }

		/// <summary>
		/// This is called when the blockmap needs to be refilled, because it was invalidated.
		/// This usually happens when geometry is changed by undo, redo, cut or paste actions.
		/// Lines and Things are added to the block map by the base implementation.
		/// </summary>
		protected virtual void FillBlockMap()
		{
			blockmap.Clear();//mxd
			blockmap.AddLinedefsSet(General.Map.Map.Linedefs);
			blockmap.AddThingsSet(General.Map.Map.Things);
			blockmap.AddSectorsSet(General.Map.Map.Sectors);
			blockmapiscurrent = true;
			blockmapchangeversion = General.Map.ChangeVersion;
			haslastsectorqueryposition = false;
			haslastcullcamera = false;
			InvalidateSkyboxVisibility();
		}

		// A skybox visibility result contains references to the current visual-sector
		// and visual-thing instances. Any map/resource refresh must force it to be
		// rebuilt even if the next camera view happens to cover the same blocks.
		protected void InvalidateSkyboxVisibility()
		{
			hasskyboxvisibility = false;
			// These collections own references to the visual objects produced by the
			// previous map state. Undo/redo may dispose those objects before the next
			// skybox cull, so release them immediately instead of allowing a redraw to
			// submit stale geometry to Renderer3D.
			if(skyboxvisiblegeometry != null) skyboxvisiblegeometry.Clear();
			if(skyboxvisiblethings != null) skyboxvisiblethings.Clear();
			if(skyboxvisibleblocks != null) skyboxvisibleblocks.Clear();
			if(skyboxcandidatevisibleblocks != null) skyboxcandidatevisibleblocks.Clear();
		}

		// Rebuilding a visual sector replaces its VisualGeometry objects. Keep the
		// current visibility set pointing at the replacements without an expensive
		// all-map cull. This makes newly exposed walls appear on the same edit frame.
		protected void RefreshVisibleSectorGeometry(ICollection<VisualSector> rebuiltsectors)
		{
			if(rebuiltsectors == null || rebuiltsectors.Count == 0 || visiblegeometry == null) return;
			InvalidateSkyboxVisibility();

			HashSet<VisualSector> rebuilt = new HashSet<VisualSector>(rebuiltsectors);
			HashSet<Linedef> currentlines = new HashSet<Linedef>(visiblelines);
			Vector2D cameraposition = General.Map.VisualCamera.Position;
			visiblegeometry.RemoveAll(g => rebuilt.Contains(g.Sector));

			foreach(VisualSector sector in rebuilt)
			{
				if(sector == null || sector.IsDisposed || sector.Sector == null || sector.Sector.IsDisposed) continue;
				if(!visiblesectors.Contains(sector)) continue;

				visiblegeometry.AddRange(sector.FixedGeometry);
				foreach(Sidedef sidedef in sector.Sector.Sidedefs)
				{
					bool includeside = false;
					if(currentlines.Contains(sidedef.Line))
					{
						Sidedef visibleside = (sidedef.Line.SideOfLine(cameraposition) < 0.0
							? sidedef.Line.Front : sidedef.Line.Back);
						includeside = object.ReferenceEquals(visibleside, sidedef);
					}
					else if(visiblelines.Count == 0 && object.ReferenceEquals(General.Map.VisualCamera.Sector, sector.Sector))
					{
						double side = sidedef.Line.SideOfLine(cameraposition);
						includeside = ((side < 0.0) && sidedef.IsFront) || ((side > 0.0) && !sidedef.IsFront);
					}

					if(!includeside) continue;
					if(!sector.SidedefGeometryReady(sidedef)) sector.PrepareSidedefGeometry(sidedef);
					visiblegeometry.AddRange(sector.GetSidedefGeometry(sidedef));
				}
			}

			if(visiblegeometry.Count > 1) visiblegeometry.Sort(VisualGeometrySectorComparer.Instance);
			visiblegeometryversion++;
		}

		// Build a separate visibility set for SRB2's live skybox camera without
		// disturbing the normal camera's retained cull lists or picking state.
		protected void UpdateSkyboxVisibility(Vector3D position)
		{
			long cullstart = Clock.CurrentTime;
			int cullinterval = 16;
			if(lastskyboxvisibilitycost > 8)
				cullinterval = (int)Math.Min(100L, Math.Max(16L, lastskyboxvisibilitycost * 2L));
			if(hasskyboxvisibility && cullstart - lastskyboxvisibilitytime < cullinterval) return;

			Vector2D position2d = position;
			blockmap.GetFrustumRange(renderer.Frustum2D, position2d, skyboxcandidatevisibleblocks);
			long blockquerydone = Clock.CurrentTime;
			bool sameblockmembership = hasskyboxvisibility
				&& SameBlockMembership(skyboxcandidatevisibleblocks, skyboxvisibleblocks);
			bool positionunchanged = hasskyboxvisibility
				&& lastskyboxvisibilityposition.x == position2d.x
				&& lastskyboxvisibilityposition.y == position2d.y;
			if(sameblockmembership && (positionunchanged
				|| !MovementChangedLineSide(lastskyboxvisibilityposition, position2d, skyboxvisibleblocks)))
			{
				lastskyboxvisibilitytime = Clock.CurrentTime;
				lastskyboxvisibilitycost = lastskyboxvisibilitytime - cullstart;
				return;
			}

			List<VisualBlockEntry> previousblocks = skyboxvisibleblocks;
			skyboxvisibleblocks = skyboxcandidatevisibleblocks;
			skyboxcandidatevisibleblocks = previousblocks;
			skyboxcandidatevisibleblocks.Clear();

			lastProcessed++;
			skyboxvisiblegeometry.Clear();
			skyboxvisiblethings.Clear();

			Sector camsector = blockmap.GetSectorAt(position2d, sectorqueryblocks, sectorquerysectors, sectorquerylinedefs);
			if(camsector != null && !allsectors.ContainsKey(camsector)) CreateVisualSector(camsector);

			foreach(VisualBlockEntry block in skyboxvisibleblocks)
			{
				if(processgeometry)
				{
					foreach(Linedef ld in block.Lines)
					{
						if(ld.LastProcessed == lastProcessed) continue;
						ld.LastProcessed = lastProcessed;
						Sidedef sd = (ld.SideOfLine(position2d) < 0 ? ld.Front : ld.Back);
						if(sd == null || sd.LastProcessed == lastProcessed) continue;
						sd.LastProcessed = lastProcessed;

						VisualSector vs;
						if(!allsectors.TryGetValue(sd.Sector, out vs) || vs == null)
							vs = CreateVisualSector(sd.Sector);
						if(vs == null) continue;

						if(sd.Sector.LastProcessed != lastProcessed)
						{
							sd.Sector.LastProcessed = lastProcessed;
							skyboxvisiblegeometry.AddRange(vs.FixedGeometry);
						}

						if(!vs.SidedefGeometryReady(sd)) vs.PrepareSidedefGeometry(sd);
						skyboxvisiblegeometry.AddRange(vs.GetSidedefGeometry(sd));
					}
				}

				if(processthings)
				{
					foreach(Thing thing in block.Things)
					{
						if(thing.LastProcessed == lastProcessed) continue;
						thing.LastProcessed = lastProcessed;
						if(thing.Type == 780) continue; // SRB2 skybox controllers are invisible in-game.
						if(!General.Map.ThingsFilter.IsThingVisible(thing)) continue;

						VisualThing visualthing;
						if(allthings.TryGetValue(thing, out visualthing))
						{
							if(visualthing != null) skyboxvisiblethings.Add(visualthing);
						}
						else
						{
							visualthing = CreateVisualThing(thing);
							allthings[thing] = visualthing;
							if(visualthing != null) skyboxvisiblethings.Add(visualthing);
						}
					}
				}
			}

			if(skyboxvisiblegeometry.Count > 1)
				skyboxvisiblegeometry.Sort(VisualGeometrySectorComparer.Instance);

			lastskyboxvisibilityposition = position2d;
			hasskyboxvisibility = true;
			long culldone = Clock.CurrentTime;
			long culltime = culldone - cullstart;
			lastskyboxvisibilitytime = culldone;
			lastskyboxvisibilitycost = culltime;
			if(culltime >= 25 && culldone - lastslowskyboxculllogtime >= 1000)
			{
				General.WriteLogLine("Slow Visual Mode skybox cull: " + culltime + " ms for "
					+ skyboxvisibleblocks.Count + " blocks and " + skyboxvisiblegeometry.Count
					+ " geometry items (block query " + (blockquerydone - cullstart)
					+ " ms, collect/build/sort " + (culldone - blockquerydone) + " ms)");
				lastslowskyboxculllogtime = culldone;
			}
		}
		
		/// <summary>
		/// While this mode is active, this is called continuously to process whatever needs processing.
		/// </summary>
		public override void OnProcess(long deltatime)
		{
			base.OnProcess(deltatime);
			
			// Camera vectors
			Vector3D camvec = Vector3D.FromAngleXYZ(General.Map.VisualCamera.AngleXY, General.Map.VisualCamera.AngleZ);
			Vector3D camvecstrafe = Vector3D.FromAngleXY(General.Map.VisualCamera.AngleXY + Angle2D.PIHALF);
			Vector3D cammovemul = General.Map.VisualCamera.MoveMultiplier;
			Vector3D camdeltapos = new Vector3D();
			Vector3D upvec = new Vector3D(0.0, 0.0, 1.0);

			double multiplier;
			if(General.Interface.ShiftState) multiplier = MOVE_SPEED_MULTIPLIER * 2.0f; else multiplier = MOVE_SPEED_MULTIPLIER;
			
			if (orbit)
			{
				if (keyforward) orbitRadius -= General.Settings.MoveSpeed * multiplier * deltatime;
				if (keybackward) orbitRadius += General.Settings.MoveSpeed * multiplier * deltatime;

				if (orbitRadius < 1) orbitRadius = 1f;

				General.Map.VisualCamera.ProcessMovement(new Vector3D());
			}
			else
			{
				// Move the camera
				
				if(keyforward) camdeltapos += camvec * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				if(keybackward) camdeltapos -= camvec * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				if(keyleft) camdeltapos -= camvecstrafe * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				if(keyright) camdeltapos += camvecstrafe * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				if(keyup) camdeltapos += upvec * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				if(keydown) camdeltapos += -upvec * cammovemul * General.Settings.MoveSpeed * multiplier * deltatime;
				
				// Move the camera
				General.Map.VisualCamera.ProcessMovement(camdeltapos);
			}

			// Apply new camera matrices
			renderer.PositionAndLookAt(General.Map.VisualCamera.Position, General.Map.VisualCamera.Target);

			// Visibility data is unchanged while the camera is stationary. At very high
			// frame caps, reuse the preceding set for intervening rendered frames. Very
			// large view distances use a 60-ish Hz cull cadence; camera rendering remains
			// uncapped, but the all-map spatial traversal no longer consumes every frame.
			Vector3D currentcullposition = General.Map.VisualCamera.Position;
			double currentcullanglexy = General.Map.VisualCamera.AngleXY;
			double currentcullanglez = General.Map.VisualCamera.AngleZ;
			bool cullviewchanged = !haslastcullcamera
				|| currentcullposition.x != lastcullcameraposition.x
				|| currentcullposition.y != lastcullcameraposition.y
				|| currentcullanglexy != lastcullcameraanglexy
				|| currentcullanglez != lastcullcameraanglez;
			int cullinterval = (General.Settings.ViewDistance >= LARGE_VIEW_DISTANCE
				? LARGE_VIEW_CULL_INTERVAL_MS : NORMAL_CULL_INTERVAL_MS);
			// Rendering the retained scene is cheap compared with rebuilding its visible
			// set. If a huge map makes a cull itself expensive, space subsequent culls far
			// enough apart to leave real frame time for camera movement. The cap keeps
			// newly revealed geometry responsive and does not alter map data or rendering.
			if(lastcullcost > cullinterval / 2)
				cullinterval = (int)Math.Min(100L, Math.Max((long)cullinterval, lastcullcost * 2L));
			long currenttime = Clock.CurrentTime;
			if(cullviewchanged
				&& (!haslastcullcamera || currenttime - lastculltime >= cullinterval))
			{
				DoCulling(true);
				lastcullcameraposition = currentcullposition;
				lastcullcameraanglexy = currentcullanglexy;
				lastcullcameraanglez = currentcullanglez;
				// Schedule from completion, not from the frame's pre-cull timestamp. An
				// expensive cull must not make the next frame immediately perform another.
				lastculltime = Clock.CurrentTime;
				haslastcullcamera = true;
			}
			
			// Update labels in main window
			General.MainWindow.UpdateCoordinates(General.Map.VisualCamera.Position);
			
			// Now redraw
			General.Interface.RedrawDisplay();
		}
		
		#endregion

		#region ================== Actions

		//mxd
		[BeginAction("centeroncoordinates", BaseAction = true)]
		protected virtual void CenterOnCoordinates() 
		{
			// Show form...
			CenterOnCoordinatesForm form = new CenterOnCoordinatesForm();
			if(form.ShowDialog() == DialogResult.OK) CenterOnCoordinates(form.Coordinates);
		}

		//mxd
		public void CenterOnCoordinates(Vector2D coords)
		{
			Sector s = General.Map.Map.GetSectorByCoordinates(coords, blockmap);

			if(s == null)
				General.Map.VisualCamera.Position = coords;
			else
				General.Map.VisualCamera.Position = new Vector3D(coords.x, coords.y, s.FloorHeight + 54);

			General.Map.VisualCamera.Sector = s;
		}

		//mxd
		[BeginAction("togglehighlight", BaseAction = true)]
		public void ToggleHighlight()
		{
			General.Settings.UseHighlight = !General.Settings.UseHighlight;

			string shorttext = "Highlight is now " + (General.Settings.UseHighlight ? "ON" : "OFF") + ".";
			string text = shorttext;

			string key = Actions.Action.GetShortcutKeyDesc(General.Actions.Current.ShortcutKey);

			if (!string.IsNullOrEmpty(key))
				text += $" Press '{key}' to toggle.";

			General.ToastManager.ShowToast("togglehighlight", ToastType.INFO, "Changed highlight", text, shorttext);
		}

		#endregion
	}
}
