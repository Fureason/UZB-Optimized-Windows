
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
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CodeImp.DoomBuilder.BuilderModes.Interface;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	[EditMode(DisplayName = "Visual Mode",
			  SwitchAction = "gzdbvisualmode", // Action name used to switch to this mode
			  ButtonImage = "VisualMode.png",	// Image resource name for the button
			  ButtonOrder = 1,					// Position of the button (lower is more to the left)
			  ButtonGroup = "001_visual",
			  UseByDefault = true)]

	public class BaseVisualMode : VisualMode
	{
		#region ================== Constants
		// Object picking
		private const long PICK_INTERVAL = 32;
		private const long PICK_INTERVAL_PAINT_SELECT = 10; // biwa
		private const long TEXTURE_COMPLETION_POLL_INTERVAL = 16;
		private const int ALIGNMENT_REFRESH_BUDGET_MS = 2;
		private const int RETAINED_REFRESH_BUDGET_MS = 4;
		private const int RETAINED_REFRESH_MAX_SECTORS_PER_FRAME = 4;
		private const int UNDO_VISIBLE_REFRESH_BUDGET_MS = 12;
		private const int UNDO_VISIBLE_REFRESH_MAX_SECTORS = 32;
		private const int SKYBOX_IDLE_REFRESH_INTERVAL_MS = 125;

		// Gravity
		private const float GRAVITY = -0.06f;
		
		#endregion
		
		#region ================== Variables

		// Gravity
		private Vector3D gravity;
		private double cameraflooroffset = 41.0;		// same as in doom
		private double cameraceilingoffset = 10.0;
		
		// Object picking
		private VisualPickResult target;
		private long lastpicktime;
		private bool haslastpickview;
		private Vector3D lastpickposition;
		private Vector3D lastpickdirection;
		private PickingMode lastpickmode;
		private long lasttexturecompletionpolltime;
		private long lasttexturecompletionversion = -1;
		private long lasttexturevisiblegeometryversion = -1;
		private bool locktarget;
		private bool useSelectionFromClassicMode;//mxd
		private readonly Timer selectioninfoupdatetimer; //mxd
		private readonly List<VisualVertex> rendervisualvertices;
		private readonly HashSet<Vertex> rendervisualvertexset;
		private long rendervisualverticesversion = -1;
		private bool slopehandlesdirty;
		private readonly Queue<Sidedef> pendingalignedsidedefs;
		private readonly HashSet<Sidedef> pendingalignedsidedefset;
		private bool alignmentrefreshactive;
		private long alignmentrefreshstart;
		private bool texturealignmentundoredo;
		private int textureeditbatchdepth;
		private bool usedtexturesupdatepending;
		private readonly Queue<Sector> pendingretainedsectors;
		private readonly HashSet<Sector> pendingretainedsectorset;
		private readonly Queue<Thing> pendingretainedthings;
		private readonly HashSet<Thing> pendingretainedthingset;
		private bool retainedrefreshactive;
		private long retainedrefreshstart;
		private int retainedrefreshinitialsectors;
		private int retainedrefreshinitialthings;
		private readonly HashSet<MapElement3D> pendingselectionrestore;
		private Sector undorefreshprioritysector;
		private readonly Thing[] skyboxviewpoints;
		private readonly Thing[] skyboxcenterpoints;
		private readonly Vector2D[] skyboxviewpointsectorpositions;
		private readonly bool[] skyboxviewpointsectorpositionsvalid;
		private readonly ToolStripLabel skyboxpreviewlabel;
		private readonly ToolStripComboBox skyboxpreviewselector;
		private readonly ToolStripLabel skyboxcenterpointlabel;
		private readonly ToolStripComboBox skyboxcenterpointselector;
		private int selectedskyboxid;
		private int selectedskyboxcenterpointid;
		private bool skyboxpreviewenabled;
		private bool updateskyboxselector;
		private bool skyboxselectorvisible;
		private bool hasskyboxframe;
		private long lastskyboxrendertime;
		private Vector3D lastskyboxcameraposition;
		private Vector3D lastskyboxcameratarget;
		private const int SKYBOX_CENTER_MATCH_VIEWPOINT = -2;
		private const int SKYBOX_CENTER_NONE = -1;

		// This keeps extra element info
		private Dictionary<Sector, SectorData> sectordata;
		private Dictionary<Thing, ThingData> thingdata;
		private Dictionary<Vertex, VertexData> vertexdata; //mxd
		private readonly HashSet<Sector> vertexoffseteffectsectors;
		//private Dictionary<Thing, EffectDynamicLight> lightdata; //mxd
		private MouseEventArgs lockedmousemoveargs;
		private MouseButtons lockedmousemovebuttons;
		
		// This is true when a selection was made because the action is performed
		// on an object that was not selected. In this case the previous selection
		// is cleared and the targeted object is temporarely selected to perform
		// the action on. After the action is completed, the object is deselected.
		private bool singleselection;
		
		// We keep these to determine if we need to make a new undo level
		private bool selectionchanged;
		private int lastundogroup;
		private VisualActionResult actionresult;
		private bool undocreated;

		// List of selected objects when an action is performed
		private List<IVisualEventReceiver> selectedobjects;
		private readonly HashSet<BaseVisualGeometrySector> changedvisualsurfaces;
		
		//mxd. Used in Cut/PasteSelection actions
		private readonly List<ThingCopyData> copybuffer;
		private Type lasthighlighttype;

		// biwa. Info for paint selection
		protected bool paintselectpressed;
		protected Type paintselecttype = null;
		private IVisualEventReceiver lastpaintselectedobject1; // Last
		private IVisualEventReceiver lastpaintselectedobject2; // Second last
		protected IVisualPickable highlighted; // biwa

		//mxd. Moved here from Tools
		private struct SidedefAlignJob
		{
			public Sidedef sidedef;

			public double offsetx;
			public double scaleX; //mxd
			public double scaleY; //mxd

			private Sidedef controlside; //mxd
			public Sidedef controlSide
			{
				get
				{
					return controlside;
				}
				set
				{
					controlside = value;
					ceilingheight = (controlside.Index != sidedef.Index && controlside.Line.Args[1] == 0 ? controlside.Sector.FloorHeight : controlside.Sector.CeilHeight);
				}
			}

			private int ceilingheight; //mxd
			public int ceilingHeight { get { return ceilingheight; } } //mxd

			// When this is true, the previous sidedef was on the left of
			// this one and the texture X offset of this sidedef can be set
			// directly. When this is false, the length of this sidedef
			// must be subtracted from the X offset first.
			public bool forward;
		}
		
		#endregion
		
		#region ================== Properties

		public override object HighlightedObject
		{
			get
			{
				// Geometry picked?
				VisualGeometry vg = target.picked as VisualGeometry;
				if(vg != null)
				{
					if(vg.Sidedef != null) return vg.Sidedef;
					if(vg.Sector != null) return vg.Sector;
					return null;
				}
				// Thing picked?
				VisualThing vt = target.picked as VisualThing;
				if(vt != null) return vt.Thing;

				return null;
			}
		}

		public object HighlightedTarget { get { return target.picked; } } //mxd
		public bool UseSelectionFromClassicMode { get { return useSelectionFromClassicMode; } } //mxd

		new public IRenderer3D Renderer { get { return renderer; } }
		
		public bool IsSingleSelection { get { return singleselection; } }
		public bool SelectionChanged { get { return selectionchanged; } set { selectionchanged |= value; } }

		public bool PaintSelectPressed { get { return paintselectpressed; } } // biwa
		public Type PaintSelectType { get { return paintselecttype; } set { paintselecttype = value; } } // biwa
		internal IVisualEventReceiver LastPaintSelectedObject1 { get { return lastpaintselectedobject1; } set { lastpaintselectedobject1 = value; } } // biwa
		internal IVisualEventReceiver LastPaintSelectedObject2 { get { return lastpaintselectedobject2; } set { lastpaintselectedobject2 = value; } } // biwa
		public IVisualPickable Highlighted { get { return highlighted; } } // biwa
		public Group3D SelectedGroup3D { get; internal set; }
		public Group3D LastSelectedGroup3D { get; internal set; }

		#endregion
		
		#region ================== Constructor / Disposer

		// Constructor
		public BaseVisualMode()
		{
			// Initialize
			this.gravity = new Vector3D(0.0f, 0.0f, 0.0f);
			this.selectedobjects = new List<IVisualEventReceiver>();
			this.changedvisualsurfaces = new HashSet<BaseVisualGeometrySector>();
			
			//mxd
			this.copybuffer = new List<ThingCopyData>();
			this.rendervisualvertices = new List<VisualVertex>();
			this.rendervisualvertexset = new HashSet<Vertex>();
			this.pendingalignedsidedefs = new Queue<Sidedef>();
			this.pendingalignedsidedefset = new HashSet<Sidedef>();
			this.pendingretainedsectors = new Queue<Sector>();
			this.pendingretainedsectorset = new HashSet<Sector>();
			this.pendingretainedthings = new Queue<Thing>();
			this.pendingretainedthingset = new HashSet<Thing>();
			this.pendingselectionrestore = new HashSet<MapElement3D>();
			this.skyboxviewpoints = new Thing[16];
			this.skyboxcenterpoints = new Thing[16];
			this.skyboxviewpointsectorpositions = new Vector2D[16];
			this.skyboxviewpointsectorpositionsvalid = new bool[16];
			this.selectedskyboxid = 0;
			this.selectedskyboxcenterpointid = SKYBOX_CENTER_MATCH_VIEWPOINT;
			this.skyboxpreviewenabled = true;
			this.skyboxpreviewlabel = new ToolStripLabel("Skybox view:");
			this.skyboxpreviewselector = new ToolStripComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoSize = false,
				Width = 190,
				ToolTipText = "Choose the SRB2 skybox viewpoint tag to preview."
			};
			this.skyboxpreviewselector.SelectedIndexChanged += SkyboxPreviewSelectorOnSelectedIndexChanged;
			this.skyboxcenterpointlabel = new ToolStripLabel("Center:");
			this.skyboxcenterpointselector = new ToolStripComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				AutoSize = false,
				Width = 170,
				ToolTipText = "Choose a different skybox centerpoint tag, match the viewpoint tag, or use none."
			};
			this.skyboxcenterpointselector.SelectedIndexChanged += SkyboxCenterpointSelectorOnSelectedIndexChanged;
			this.vertexoffseteffectsectors = new HashSet<Sector>();
			this.slopehandlesdirty = true;
			this.selectioninfoupdatetimer = new Timer();
			selectioninfoupdatetimer.Interval = 100;
			selectioninfoupdatetimer.Tick += SelectioninfoupdatetimerOnTick;
			
			// We have no destructor
			GC.SuppressFinalize(this);
		}

		// Disposer
		public override void Dispose()
		{
			// Not already disposed?
			if(!isdisposed)
			{
				// Clean up
				HideSkyboxSelector();
				selectioninfoupdatetimer.Dispose(); //mxd
				rendervisualvertices.Clear();
				rendervisualvertexset.Clear();
				pendingalignedsidedefs.Clear();
				pendingalignedsidedefset.Clear();
				pendingretainedsectors.Clear();
				pendingretainedsectorset.Clear();
				pendingretainedthings.Clear();
				pendingretainedthingset.Clear();
				pendingselectionrestore.Clear();
				skyboxpreviewselector.SelectedIndexChanged -= SkyboxPreviewSelectorOnSelectedIndexChanged;
				skyboxcenterpointselector.SelectedIndexChanged -= SkyboxCenterpointSelectorOnSelectedIndexChanged;
				skyboxpreviewselector.Dispose();
				skyboxpreviewlabel.Dispose();
				skyboxcenterpointselector.Dispose();
				skyboxcenterpointlabel.Dispose();
				
				// Done
				base.Dispose();
			}
		}

		#endregion
		
		#region ================== Methods

		// This calculates brightness level
		internal int CalculateBrightness(int level)
		{
			return renderer.CalculateBrightness(level);
		}

		private void RefreshSkyboxObjects()
		{
			hasskyboxframe = false;
			InvalidateSkyboxVisibility();
			Array.Clear(skyboxviewpoints, 0, skyboxviewpoints.Length);
			Array.Clear(skyboxcenterpoints, 0, skyboxcenterpoints.Length);
			Array.Clear(skyboxviewpointsectorpositionsvalid, 0, skyboxviewpointsectorpositionsvalid.Length);

			foreach(Thing thing in General.Map.Map.Things)
			{
				if(thing == null || thing.IsDisposed || thing.Type != 780 || thing.Tag < 0 || thing.Tag > 15) continue;
				if(thing.Args[0] == 0) skyboxviewpoints[thing.Tag] = thing;
				else skyboxcenterpoints[thing.Tag] = thing;
			}

			updateskyboxselector = true;
			skyboxpreviewselector.Items.Clear();
			skyboxcenterpointselector.Items.Clear();
			skyboxpreviewselector.Items.Add(new SkyboxPreviewChoice(-1, "Off (regular sky texture)"));
			skyboxcenterpointselector.Items.Add(new SkyboxPreviewChoice(SKYBOX_CENTER_MATCH_VIEWPOINT, "Match viewpoint tag"));
			skyboxcenterpointselector.Items.Add(new SkyboxPreviewChoice(SKYBOX_CENTER_NONE, "None"));
			int selectedindex = 0;
			int selectedcenterindex = (selectedskyboxcenterpointid == SKYBOX_CENTER_NONE ? 1 : 0);
			int firstviewpointid = -1;
			for(int id = 0; id < skyboxviewpoints.Length; id++)
			{
				Thing viewpoint = skyboxviewpoints[id];
				if(viewpoint != null)
				{
					if(firstviewpointid < 0) firstviewpointid = id;
					int index = skyboxpreviewselector.Items.Add(new SkyboxPreviewChoice(id,
						"Tag " + id + " — thing " + viewpoint.Index));
					if(skyboxpreviewenabled && id == selectedskyboxid) selectedindex = index;
				}

				Thing centerpoint = skyboxcenterpoints[id];
				if(centerpoint != null)
				{
					int index = skyboxcenterpointselector.Items.Add(new SkyboxPreviewChoice(id,
						"Tag " + id + " — thing " + centerpoint.Index));
					if(id == selectedskyboxcenterpointid) selectedcenterindex = index;
				}
			}

			// SRB2 begins with ID 0. If this map only defines alternate IDs, leave
			// the live preview off until the mapper explicitly selects one.
			if(selectedskyboxid < 0 || selectedskyboxid >= skyboxviewpoints.Length
				|| skyboxviewpoints[selectedskyboxid] == null)
			{
				selectedskyboxid = firstviewpointid;
				skyboxpreviewenabled = false;
				selectedindex = 0;
			}
			if(selectedskyboxcenterpointid >= 0
				&& (selectedskyboxcenterpointid >= skyboxcenterpoints.Length
					|| skyboxcenterpoints[selectedskyboxcenterpointid] == null))
			{
				selectedskyboxcenterpointid = SKYBOX_CENTER_MATCH_VIEWPOINT;
				selectedcenterindex = 0;
			}
			skyboxpreviewselector.SelectedIndex = selectedindex;
			skyboxcenterpointselector.SelectedIndex = selectedcenterindex;
			updateskyboxselector = false;

			bool hasviewpoint = skyboxpreviewselector.Items.Count > 1;
			if(hasviewpoint && !skyboxselectorvisible)
			{
				General.Interface.AddButton(skyboxpreviewlabel);
				General.Interface.AddButton(skyboxpreviewselector);
				General.Interface.AddButton(skyboxcenterpointlabel);
				General.Interface.AddButton(skyboxcenterpointselector);
				skyboxselectorvisible = true;
			}
			else if(!hasviewpoint)
			{
				HideSkyboxSelector();
			}
		}

		private void HideSkyboxSelector()
		{
			if(!skyboxselectorvisible) return;
			General.Interface.RemoveButton(skyboxpreviewselector);
			General.Interface.RemoveButton(skyboxpreviewlabel);
			General.Interface.RemoveButton(skyboxcenterpointselector);
			General.Interface.RemoveButton(skyboxcenterpointlabel);
			skyboxselectorvisible = false;
		}

		private void RefreshInvisibleFOFPreview()
		{
			foreach(VisualSector vs in allsectors.Values)
			{
				BaseVisualSector sector = vs as BaseVisualSector;
				if(sector != null) sector.Changed = true;
			}

			RebuildElementData();
			UpdateChangedObjects();
			General.Interface.RedrawDisplay();
		}

		private void RefreshInvisibleFOFPreview(IEnumerable<Linedef> controllines)
		{
			if(!General.Settings.SRB2RenderingEffects || controllines == null
				|| sectordata == null || allsectors == null) return;

			ResetChangedVisualSurfaces();
			List<VisualSector> rebuiltsectors = RebuildFOFSectorsForControlLines(controllines);
			if(rebuiltsectors.Count > 0)
			{
				RefreshVisibleSectorGeometry(rebuiltsectors);
				InvalidatePickTarget(true);
				ShowTargetInfo();
				General.Interface.RedrawDisplay();
			}
		}

		private List<VisualSector> RebuildFOFSectorsForControlLines(IEnumerable<Linedef> controllines)
		{
			List<VisualSector> rebuiltsectors = new List<VisualSector>();
			if(controllines == null || sectordata == null || allsectors == null) return rebuiltsectors;

			HashSet<Linedef> controls = new HashSet<Linedef>(controllines.Where(line =>
				line != null && !line.IsDisposed));
			if(controls.Count == 0) return rebuiltsectors;

			// Updating one affected SectorData can discover another dependency and add it
			// to sectordata. Find all targets from a stable snapshot before updating any
			// of them, otherwise undo/redo can modify the dictionary during enumeration.
			List<KeyValuePair<Sector, SectorData>> datasnapshot = sectordata.ToList();
			Dictionary<Sector, SectorData> affectedtargets = new Dictionary<Sector, SectorData>();
			foreach(KeyValuePair<Sector, SectorData> pair in datasnapshot)
			{
				if(pair.Key == null || pair.Key.IsDisposed || pair.Value == null) continue;
				bool affected = false;
				foreach(Effect3DFloor floor in pair.Value.ExtraFloors)
				{
					if(controls.Contains(floor.Linedef))
					{
						affected = true;
						break;
					}
				}

				if(affected) affectedtargets[pair.Key] = pair.Value;
			}

			if(affectedtargets.Count == 0) return rebuiltsectors;

			foreach(KeyValuePair<Sector, SectorData> pair in affectedtargets)
			{
				SectorData currentdata;
				if(sectordata.TryGetValue(pair.Key, out currentdata) && currentdata != null)
					currentdata.UpdateForced();
			}

			// A visible FOF wall belongs to the visual sector on one side of the boundary,
			// but reads ExtraFloors from the sector on the other side. Rebuild both the
			// tagged targets and their neighbours so retained selected walls cannot keep
			// the pre-undo missing texture.
			HashSet<Sector> affectedvisualsectors = new HashSet<Sector>(affectedtargets.Keys);
			foreach(Sector targetsector in affectedtargets.Keys)
			{
				foreach(Sidedef side in targetsector.Sidedefs)
				{
					if(side != null && !side.IsDisposed && side.Other != null
						&& side.Other.Sector != null && !side.Other.Sector.IsDisposed)
						affectedvisualsectors.Add(side.Other.Sector);
				}
			}

			foreach(Sector affectedsector in affectedvisualsectors)
			{
				VisualSector visualsector;
				if(allsectors.TryGetValue(affectedsector, out visualsector) && visualsector != null)
				{
					BaseVisualSector sector = visualsector as BaseVisualSector;
					if(sector != null)
					{
						sector.Rebuild();
						rebuiltsectors.Add(sector);
					}
				}
			}

			return rebuiltsectors;
		}

		private void SkyboxPreviewSelectorOnSelectedIndexChanged(object sender, EventArgs e)
		{
			if(updateskyboxselector) return;
			SkyboxPreviewChoice choice = skyboxpreviewselector.SelectedItem as SkyboxPreviewChoice;
			if(choice == null) return;
			if(choice.Id < 0)
			{
				skyboxpreviewenabled = false;
			}
			else
			{
				selectedskyboxid = choice.Id;
				skyboxpreviewenabled = true;
			}
			hasskyboxframe = false;
			InvalidateSkyboxVisibility();
			General.Interface.RedrawDisplay();
		}

		private void SkyboxCenterpointSelectorOnSelectedIndexChanged(object sender, EventArgs e)
		{
			if(updateskyboxselector) return;
			SkyboxPreviewChoice choice = skyboxcenterpointselector.SelectedItem as SkyboxPreviewChoice;
			if(choice == null) return;
			selectedskyboxcenterpointid = choice.Id;
			hasskyboxframe = false;
			InvalidateSkyboxVisibility();
			General.Interface.RedrawDisplay();
		}

		private void SetSkyboxPreviewSelection(int id, bool enabled)
		{
			if(enabled && (id < 0 || id >= skyboxviewpoints.Length || skyboxviewpoints[id] == null)) enabled = false;
			if(id >= 0 && id < skyboxviewpoints.Length && skyboxviewpoints[id] != null) selectedskyboxid = id;
			skyboxpreviewenabled = enabled;
			hasskyboxframe = false;
			InvalidateSkyboxVisibility();

			updateskyboxselector = true;
			int wantedid = (enabled ? selectedskyboxid : -1);
			for(int i = 0; i < skyboxpreviewselector.Items.Count; i++)
			{
				SkyboxPreviewChoice choice = skyboxpreviewselector.Items[i] as SkyboxPreviewChoice;
				if(choice != null && choice.Id == wantedid)
				{
					skyboxpreviewselector.SelectedIndex = i;
					break;
				}
			}
			updateskyboxselector = false;
			General.Interface.RedrawDisplay();
		}

		private void SetSkyboxCenterpointSelection(int id)
		{
			if(id >= 0 && (id >= skyboxcenterpoints.Length || skyboxcenterpoints[id] == null))
				id = SKYBOX_CENTER_MATCH_VIEWPOINT;

			selectedskyboxcenterpointid = id;
			hasskyboxframe = false;
			InvalidateSkyboxVisibility();

			updateskyboxselector = true;
			for(int i = 0; i < skyboxcenterpointselector.Items.Count; i++)
			{
				SkyboxPreviewChoice choice = skyboxcenterpointselector.Items[i] as SkyboxPreviewChoice;
				if(choice != null && choice.Id == id)
				{
					skyboxcenterpointselector.SelectedIndex = i;
					break;
				}
			}
			updateskyboxselector = false;
			General.Interface.RedrawDisplay();
		}

		private string GetSkyboxPreviewDescription(int id)
		{
			Thing viewpoint = (id >= 0 && id < skyboxviewpoints.Length ? skyboxviewpoints[id] : null);
			Thing centerpoint = GetSelectedSkyboxCenterpoint(id);
			if(viewpoint == null) return "No skybox viewpoints are available.";
			int centerid = (selectedskyboxcenterpointid == SKYBOX_CENTER_MATCH_VIEWPOINT
				? id : selectedskyboxcenterpointid);
			return "Skybox viewpoint tag " + id + " (thing " + viewpoint.Index + ")"
				+ (centerpoint != null ? " with centerpoint tag " + centerid + " (thing " + centerpoint.Index + ")."
					: "; no centerpoint selected.");
		}

		private Thing GetSelectedSkyboxCenterpoint(int viewpointid)
		{
			int id = (selectedskyboxcenterpointid == SKYBOX_CENTER_MATCH_VIEWPOINT
				? viewpointid : selectedskyboxcenterpointid);
			if(id < 0 || id >= skyboxcenterpoints.Length) return null;
			Thing centerpoint = skyboxcenterpoints[id];
			return (centerpoint != null && !centerpoint.IsDisposed ? centerpoint : null);
		}

		private void CycleSkyboxPreview(int direction)
		{
			List<int> available = new List<int>(skyboxviewpoints.Length);
			for(int id = 0; id < skyboxviewpoints.Length; id++)
				if(skyboxviewpoints[id] != null) available.Add(id);

			if(available.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No skybox viewpoints are available in this map.");
				return;
			}

			int index = (skyboxpreviewenabled ? available.IndexOf(selectedskyboxid) : (direction > 0 ? -1 : 0));
			index = (index + direction + available.Count) % available.Count;
			SetSkyboxPreviewSelection(available[index], true);
			General.Interface.DisplayStatus(StatusType.Info, GetSkyboxPreviewDescription(available[index]));
		}

		private void CycleSkyboxCenterpoint(int direction)
		{
			List<int> available = new List<int>(skyboxcenterpoints.Length);
			for(int id = 0; id < skyboxcenterpoints.Length; id++)
				if(skyboxcenterpoints[id] != null) available.Add(id);

			if(available.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No skybox centerpoints are available in this map.");
				return;
			}

			int index = available.IndexOf(selectedskyboxcenterpointid);
			if(index < 0) index = (direction > 0 ? -1 : 0);
			index = (index + direction + available.Count) % available.Count;
			int selectedid = available[index];
			SetSkyboxCenterpointSelection(selectedid);
			Thing centerpoint = skyboxcenterpoints[selectedid];
			General.Interface.DisplayStatus(StatusType.Info,
				"Skybox centerpoint tag " + selectedid + " (thing " + centerpoint.Index + ").");
		}

		[BeginAction("toggleskyboxpreview")]
		public void ToggleSkyboxPreview()
		{
			if(skyboxpreviewenabled)
			{
				SetSkyboxPreviewSelection(selectedskyboxid, false);
				General.Interface.DisplayStatus(StatusType.Info, "Live skybox preview disabled.");
				return;
			}

			int id = selectedskyboxid;
			if(id < 0 || id >= skyboxviewpoints.Length || skyboxviewpoints[id] == null)
			{
				id = -1;
				for(int i = 0; i < skyboxviewpoints.Length; i++)
					if(skyboxviewpoints[i] != null) { id = i; break; }
			}

			if(id < 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No skybox viewpoints are available in this map.");
				return;
			}

			SetSkyboxPreviewSelection(id, true);
			General.Interface.DisplayStatus(StatusType.Info, GetSkyboxPreviewDescription(id));
		}

		[BeginAction("cycleskyboxviewpoint")]
		public void CycleSkyboxViewpoint() { CycleSkyboxPreview(1); }

		[BeginAction("cycleskyboxcenterpoint")]
		public void CycleSkyboxCenterpoint() { CycleSkyboxCenterpoint(1); }

		[BeginAction("matchskyboxcenterpoint")]
		public void MatchSkyboxCenterpoint()
		{
			SetSkyboxCenterpointSelection(SKYBOX_CENTER_MATCH_VIEWPOINT);
			General.Interface.DisplayStatus(StatusType.Info,
				"Skybox centerpoint now matches the selected viewpoint tag.");
		}

		private double GetSkyboxThingZ(Thing thing, int skyboxid)
		{
			Vector2D currentposition = thing.Position;
			if(!skyboxviewpointsectorpositionsvalid[skyboxid]
				|| skyboxviewpointsectorpositions[skyboxid] != currentposition)
			{
				// A skybox viewpoint is normally stationary. Determine its containing sector
				// only when first used or moved instead of allocating point-query scratch
				// collections on every rendered skybox frame.
				thing.DetermineSector(blockmap);
				skyboxviewpointsectorpositions[skyboxid] = currentposition;
				skyboxviewpointsectorpositionsvalid[skyboxid] = true;
			}
			ThingTypeInfo info = General.Map.Data.GetThingInfo(thing.Type);
			if(thing.AbsoluteZ || (info != null && info.AbsoluteZ) || thing.Sector == null)
				return thing.Position.z;

			return thing.Position.z + Sector.GetFloorPlane(thing.Sector).GetZ(thing.Position);
		}

		private static double ApplySkyboxScale(double value, int scale)
		{
			if(scale > 0) return value / scale;
			if(scale < 0) return value * -scale;
			return 0.0;
		}

		private bool TryGetSkyboxView(out Vector3D position, out Vector3D targetposition)
		{
			position = new Vector3D();
			targetposition = new Vector3D();
			if(!skyboxpreviewenabled) return false;
			if(selectedskyboxid < 0 || selectedskyboxid >= skyboxviewpoints.Length) return false;

			Thing viewpoint = skyboxviewpoints[selectedskyboxid];
			if(viewpoint == null || viewpoint.IsDisposed)
			{
				RefreshSkyboxObjects();
				viewpoint = (selectedskyboxid >= 0 ? skyboxviewpoints[selectedskyboxid] : null);
				if(viewpoint == null || viewpoint.IsDisposed) return false;
			}

			Vector3D camera = General.Map.VisualCamera.Position;
			position = new Vector3D(viewpoint.Position.x, viewpoint.Position.y, GetSkyboxThingZ(viewpoint, selectedskyboxid));
			double skyboxrotation = Angle2D.DegToRad(viewpoint.AngleDoom);
			Thing centerpoint = GetSelectedSkyboxCenterpoint(selectedskyboxid);
			if(centerpoint != null)
			{
				Vector2D offset = new Vector2D(
					ApplySkyboxScale(camera.x - centerpoint.Position.x, General.Map.Data.MapInfo.SkyboxScaleX),
					ApplySkyboxScale(camera.y - centerpoint.Position.y, General.Map.Data.MapInfo.SkyboxScaleY));
				offset = offset.GetRotated(skyboxrotation);
				position.x += offset.x;
				position.y += offset.y;
			}

			// SRB2 intentionally scales the absolute player-view Z, not a delta from
			// the centerpoint's Z.
			position.z += ApplySkyboxScale(camera.z, General.Map.Data.MapInfo.SkyboxScaleZ);
			double angle = General.Map.VisualCamera.AngleXY + skyboxrotation;
			Vector3D direction = Vector3D.FromAngleXYZ(angle, General.Map.VisualCamera.AngleZ) * 100.0;
			targetposition = position + direction;
			return true;
		}

		//mxd. This calculates brightness level with doom-style shading
		internal int CalculateBrightness(int level, Sidedef sd) 
		{
			return renderer.CalculateBrightness(level, sd);
		}
		
		// This adds a selected object
		internal void AddSelectedObject(IVisualEventReceiver obj)
		{
			selectedobjects.Add(obj);
			lastpaintselectedobject2 = lastpaintselectedobject1;
			lastpaintselectedobject1 = obj;
			selectionchanged = true;
			selectioninfoupdatetimer.Start(); //mxd
		}
		
		// This removes a selected object
		internal void RemoveSelectedObject(IVisualEventReceiver obj)
		{
			selectedobjects.Remove(obj);
			if (obj == lastpaintselectedobject1)
			{
				lastpaintselectedobject1 = lastpaintselectedobject2;
				lastpaintselectedobject2 = null;
			}
			selectionchanged = true;
			selectioninfoupdatetimer.Start(); //mxd
		}
		
		// This is called before an action is performed
		public void PreAction(int multiselectionundogroup)
		{
			actionresult = new VisualActionResult();

			PickTargetUnlocked();

			// If the action is not performed on a selected object, clear the
			// current selection and make a temporary selection for the target.
			if ((target.picked != null) && !target.picked.Selected && (BuilderPlug.Me.VisualModeClearSelection || (selectedobjects.Count == 0)))
			{
				// Single object, no selection
				singleselection = true;

				// Only clear the selection if anything is selected, since it can be very time consuming on huge maps
				if(BuilderPlug.Me.VisualModeClearSelection && selectedobjects.Count > 0)
					ClearSelection();

				undocreated = false;
			}
			else
			{
				singleselection = false;
				
				// Check if we should make a new undo level
				// We don't want to do this if this is the same action with the same
				// selection and the action wants to group the undo levels
				if((lastundogroup != multiselectionundogroup) || (lastundogroup == UndoGroup.None) ||
				   (multiselectionundogroup == UndoGroup.None) || selectionchanged)
				{
					// We want to create a new undo level, but not just yet
					lastundogroup = multiselectionundogroup;
					undocreated = false;
				}
				else
				{
					// We don't want to make a new undo level (changes will be combined)
					undocreated = true;
				}
			}
		}

		// Called before an action is performed. This does not make an undo level
		private void PreActionNoChange()
		{
			actionresult = new VisualActionResult();
			singleselection = false;
			undocreated = false;
		}
		
		// This is called after an action is performed
		private void PostAction()
		{
			PostAction(true, true, false);
		}

		// Selection-only actions do not modify map geometry or the inspected target.
		// Skipping the global changed-object pass here avoids walking every visible
		// sector and thing twice for a single mouse click on large maps.
		private void PostAction(bool updategeometry, bool updateinfo)
		{
			PostAction(updategeometry, updateinfo, false);
		}

		// forcevisibilityrefresh is used for sector-height edits. Those edits can
		// expose a wall that was not part of the previous visibility list, so merely
		// replacing already-visible geometry is insufficient while the camera is still.
		private void PostAction(bool updategeometry, bool updateinfo, bool forcevisibilityrefresh)
		{
			if(!string.IsNullOrEmpty(actionresult.displaystatus))
				General.Interface.DisplayStatus(StatusType.Action, actionresult.displaystatus);
			
			selectionchanged = false;

			// Only clear the selection if anything is selected, since it can be very time consuming on huge maps
			if (singleselection && selectedobjects.Count > 0) ClearSelection();
			
			if(updategeometry)
			{
				UpdateChangedObjects();
				if(forcevisibilityrefresh)
				{
					DoCulling();
					PickTargetUnlocked();
				}
				ResetChangedVisualSurfaces();
				// Map-edit actions can run while the camera is completely still. The
				// status bar repaints independently, but the Visual Mode surface would
				// otherwise keep its previous frame until the next timer/camera update.
				// Present the rebuilt floor, ceiling and wall geometry immediately.
				General.Interface.RedrawDisplay();
			}
			if(updateinfo)
			{
				// Repeated wheel edits are grouped into one undo level, so the map's
				// general change version does not advance for every step. The information
				// panel caches by that version and otherwise keeps showing the first floor
				// or ceiling height even though the geometry is already moving. Explicitly
				// invalidate that cache before showing the current target.
				General.Interface.RefreshInfo();
				ShowTargetInfo();
			}
		}

		// Used by the live sector-properties form, whose modal loop pauses normal
		// Visual Mode frame processing.
		internal void RefreshChangedGeometryImmediately()
		{
			UpdateChangedObjects();
			DoCulling();
			PickTargetUnlocked();
			ResetChangedVisualSurfaces();
			General.Interface.RedrawDisplay();
		}

		// Updates only visual things which have actually been materialized. This is
		// substantially cheaper than scanning every Thing in the map for a local
		// sector edit, while preserving the exact same visible result.
		internal void UpdateVisualThingsInSector(Sector sector, bool rebuild)
		{
			if(sector == null || sector.IsDisposed) return;

			foreach(KeyValuePair<Thing, VisualThing> pair in allthings)
			{
				Thing thing = pair.Key;
				BaseVisualThing visualthing = pair.Value as BaseVisualThing;
				if(thing == null || thing.IsDisposed || visualthing == null || thing.Sector != sector) continue;

				if(rebuild)
					visualthing.Rebuild();
				else
					visualthing.Changed = true;
			}
		}

		internal void RegisterChangedVisualSurface(BaseVisualGeometrySector surface)
		{
			if(surface != null) changedvisualsurfaces.Add(surface);
		}

		private void ResetChangedVisualSurfaces()
		{
			foreach(BaseVisualGeometrySector surface in changedvisualsurfaces)
				surface.Changed = false;

			changedvisualsurfaces.Clear();
		}
		
		// This sets the result for an action
		public void SetActionResult(VisualActionResult result)
		{
			actionresult = result;
		}

		// This sets the result for an action
		public void SetActionResult(string displaystatus)
		{
			actionresult = new VisualActionResult {displaystatus = displaystatus};
		}
		
		// This creates an undo, when only a single selection is made
		// When a multi-selection is made, the undo is created by the PreAction function
		public int CreateUndo(string description, int group, int grouptag)
		{
			if(!undocreated)
			{
				undocreated = true;

				if(singleselection)
					return General.Map.UndoRedo.CreateUndo(description, this, group, grouptag);
				return General.Map.UndoRedo.CreateUndo(description, this, UndoGroup.None, 0);
			}

			return 0;
		}

		// This creates an undo, when only a single selection is made
		// When a multi-selection is made, the undo is created by the PreAction function
		public int CreateUndo(string description)
		{
			return CreateUndo(description, UndoGroup.None, 0);
		}

		// This makes a list of the selected object
		private void RebuildSelectedObjectsList()
		{
			// Make list of selected objects
			selectedobjects = new List<IVisualEventReceiver>();
			foreach(KeyValuePair<Sector, VisualSector> vs in allsectors)
			{
				if(vs.Value != null)
				{
					BaseVisualSector bvs = (BaseVisualSector)vs.Value;
					if((bvs.Floor != null) && bvs.Floor.Selected) selectedobjects.Add(bvs.Floor);
					if((bvs.Ceiling != null) && bvs.Ceiling.Selected) selectedobjects.Add(bvs.Ceiling);
					
					// Also check extra floors
					if (bvs.ExtraFloors.Count > 0)
						foreach (VisualFloor vf in bvs.ExtraFloors)
							if (vf.Selected) selectedobjects.Add(vf);

					if (bvs.ExtraBackFloors.Count > 0)
						foreach (VisualFloor vf in bvs.ExtraBackFloors)
							if (vf.Selected) selectedobjects.Add(vf);

					// Also check extra ceilings
					if (bvs.ExtraCeilings.Count > 0)
						foreach (VisualCeiling vc in bvs.ExtraCeilings)
							if (vc.Selected) selectedobjects.Add(vc);

					if (bvs.ExtraBackCeilings.Count > 0)
						foreach (VisualCeiling vc in bvs.ExtraBackCeilings)
							if (vc.Selected) selectedobjects.Add(vc);

					foreach (Sidedef sd in vs.Key.Sidedefs)
					{
						List<VisualGeometry> sidedefgeos = bvs.GetSidedefGeometry(sd);
						foreach(VisualGeometry sdg in sidedefgeos)
						{
							if(sdg.Selected) selectedobjects.Add((IVisualEventReceiver)sdg);
						}
					}
				}
			}

			foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
			{
				if(vt.Value != null)
				{
					BaseVisualThing bvt = (BaseVisualThing)vt.Value;
					if(bvt.Selected) selectedobjects.Add(bvt);
				}
			}

			//mxd
			if(General.Map.UDMF && General.Map.Config.VertexHeightSupport && General.Settings.GZShowVisualVertices)
			{
				foreach(KeyValuePair<Vertex, VisualVertexPair> pair in vertices) 
				{
					if(pair.Value.CeilingVertex.Selected)
						selectedobjects.Add((BaseVisualVertex)pair.Value.CeilingVertex);
					if(pair.Value.FloorVertex.Selected)
						selectedobjects.Add((BaseVisualVertex)pair.Value.FloorVertex);
				}
			}

			if (General.Map.UDMF)
			{
				foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in allslopehandles)
				{
					foreach (BaseVisualSlope handle in kvp.Value)
						if (handle.Selected) selectedobjects.Add(handle);
				}
			}

			//mxd
			UpdateSelectionInfo();
		}

		//mxd. Need this to apply changes to 3d-floor even if control sector doesn't exist as BaseVisualSector
		internal BaseVisualSector CreateBaseVisualSector(Sector s)
		{
			EnsureVertexOffsetEffect(s);
			BaseVisualSector vs = new BaseVisualSector(this, s);
			allsectors.Add(s, vs);
			return vs;
		}

		// This creates a visual sector
		protected override VisualSector CreateVisualSector(Sector s)
		{
			EnsureVertexOffsetEffect(s);
			BaseVisualSector vs = new BaseVisualSector(this, s);
			allsectors.Add(s, vs); //mxd
			return vs;
		}

		internal VisualSlope CreateVisualSlopeHandle(SectorLevel level, Sidedef sd, bool up)
		{
			VisualSidedefSlope handle = new VisualSidedefSlope(this, level, sd, up);

			if (!allslopehandles.ContainsKey(sd.Sector))
				allslopehandles.Add(sd.Sector, new List<VisualSlope>());

			if (!sidedefslopehandles.ContainsKey(sd.Sector))
				sidedefslopehandles.Add(sd.Sector, new List<VisualSlope>());

			allslopehandles[sd.Sector].Add(handle);
			sidedefslopehandles[sd.Sector].Add(handle);

			return handle;
		}

		internal VisualSlope CreateVisualSlopeHandle(SectorLevel level, Vertex v, Sector s, bool up)
		{
			VisualVertexSlope handle = new VisualVertexSlope(this, level, v, s, up);

			/*
			if (!allslopehandles.ContainsKey(level.sector))
				allslopehandles.Add(level.sector, new List<VisualSlope>());

			if (!vertexslopehandles.ContainsKey(level.sector))
				vertexslopehandles.Add(level.sector, new List<VisualSlope>());

			allslopehandles[level.sector].Add(handle);
			vertexslopehandles[level.sector].Add(handle);
			*/

			if (!allslopehandles.ContainsKey(s))
				allslopehandles.Add(s, new List<VisualSlope>());

			if (!vertexslopehandles.ContainsKey(s))
				vertexslopehandles.Add(s, new List<VisualSlope>());

			allslopehandles[s].Add(handle);
			vertexslopehandles[s].Add(handle);

			return handle;
		}

		// This creates a visual thing
		protected override VisualThing CreateVisualThing(Thing t)
		{
			BaseVisualThing vt = new BaseVisualThing(this, t);
			return vt.Setup() ? vt : null;
		}

		internal BaseVisualSector GetBaseVisualSector(Sector s)
		{
			return GetVisualSector(s) as BaseVisualSector;
		}

		// This locks the target so that it isn't changed until unlocked
		public void LockTarget()
		{
			locktarget = true;
		}
		
		// This unlocks the target so that is changes to the aimed geometry again
		public void UnlockTarget()
		{
			locktarget = false;
		}
		
		// This picks a new target, if not locked
		private void PickTargetUnlocked()
		{
			if(!locktarget) PickTarget();
		}

		// Geometry can change while the camera remains perfectly still. Invalidate
		// the stationary-view pick cache so the crosshair never keeps pointing at a
		// wall object that was rebuilt or whose bounds/texture coordinates changed.
		private void InvalidatePickTarget(bool repicknow)
		{
			haslastpickview = false;
			if(repicknow) PickTargetUnlocked();
		}
		
		// This picks a new target
		private void PickTarget()
		{
			// Find the object we are aiming at
			Vector3D start = General.Map.VisualCamera.Position;
			Vector3D delta = PickingDir;
			delta = delta.GetFixedLength(General.Settings.ViewDistance * PICK_RANGE);
			VisualPickResult newtarget = PickObject(start, start + delta);
			VisualSlope pickedhandle = null;
			
			// Should we update the info on panels?
			bool updateinfo = (newtarget.picked != target.picked);

			// Operating on slope handles is potentially expensive, so only do it it absolutely necessary (i.e. when a new slope handle was selected)
			if (updateinfo)
			{
				if (target.picked is VisualSlope) // Old target
				{
					// Remove all smart pivot handles from being processed. There should only be exactly one, but better save than sorry
					List<VisualSlope> sph = new List<VisualSlope>();

					foreach (VisualSlope vs in usedslopehandles)
					{
						if(vs.SmartPivot && !(vs.Selected || vs.Pivot))
							sph.Add(vs);

						vs.SmartPivot = false;
					}

					foreach (VisualSlope vs in sph)
						usedslopehandles.Remove(vs);

					// Don't render old slope handle anymore
					if (!((VisualSlope)target.picked).Selected && !((VisualSlope)target.picked).Pivot)
						usedslopehandles.Remove((VisualSlope)target.picked);
				}

				if(newtarget.picked is VisualSlope)
				{
					usedslopehandles.Add((VisualSlope)newtarget.picked);

					pickedhandle = ((VisualSlope)newtarget.picked);
				}
			}

			// Apply new target
			target = newtarget;

			// Keep the lightweight highlighted-object state in sync with picking even
			// when no drag/paint gesture is active. The old synthetic OnMouseMove call
			// did this indirectly every frame, along with considerably more UI work.
			if(target.picked is VisualGeometry)
				highlighted = (VisualGeometry)target.picked;
			else if(target.picked is VisualThing)
				highlighted = (VisualThing)target.picked;
			else
				highlighted = null;

			// The surface under the crosshair must never wait behind the background
			// preload or the per-frame bulk GPU upload budget. Decode it at visible
			// priority, then create its one texture immediately once decoding completes.
			// This restores the original editor's near-instant aimed-surface response
			// without allowing an entire newly visible area to stall one frame.
			VisualGeometry aimedgeometry = target.picked as VisualGeometry;
			if(aimedgeometry != null && aimedgeometry.Texture != null)
			{
				ImageData aimedtexture = aimedgeometry.Texture;
				aimedtexture.RequestImmediateLoad();
				if(aimedtexture.IsImageLoaded)
				{
					Texture uploadedtexture = General.Settings.ClassicRendering
						? aimedtexture.IndexedTexture : aimedtexture.Texture;
				}
			}

			// Get the smart pivot handle for the targeted slope handle, so that it can be drawn. We have to do it after the current
			// target is set because otherwise it might get wrong results if the old target was a floor/ceiling
			if (pickedhandle != null)
			{
				VisualSlope handle = pickedhandle.GetSmartPivotHandle();
				if (handle != null)
				{
					handle.SmartPivot = true;
					usedslopehandles.Add(handle);
				}
			}

			// Show target info
			if (updateinfo)
				ShowTargetInfo();

			// Remember the exact view used for this pick. Actions force a fresh pick
			// before operating, while the regular processing loop can now avoid tracing
			// the same stationary ray dozens of times per second.
			lastpickposition = start;
			lastpickdirection = PickingDir;
			lastpickmode = pickingmode;
			haslastpickview = true;
		}

		// This shows the picked target information
		public void ShowTargetInfo()
		{
			// Any result?
			if(target.picked != null)
			{
				// Geometry picked?
				if(target.picked is VisualGeometry)
				{
					VisualGeometry pickedgeo = (VisualGeometry)target.picked;
					
					// Sidedef?
					if(pickedgeo is BaseVisualGeometrySidedef)
					{
						BaseVisualGeometrySidedef pickedsidedef = (BaseVisualGeometrySidedef)pickedgeo;
						Linedef controlline = pickedsidedef.GetControlLinedef();
						Sidedef infoside = pickedsidedef.Sidedef;

						// A 3D-floor wall is rendered on a target-sector sidedef, but its
						// editable texture and action live on the control linedef. Passing the
						// target side alongside the control line left neither side highlighted
						// in the information panel. Always choose a side owned by the line that
						// is actually being displayed.
						if(controlline != null && (infoside == null || infoside.Line != controlline))
							infoside = (controlline.Front != null ? controlline.Front : controlline.Back);

						General.Interface.ShowLinedefInfo(controlline, infoside); //mxd
					}
					// Sector?
					else if(pickedgeo is BaseVisualGeometrySector)
					{
						BaseVisualGeometrySector pickedsector = (BaseVisualGeometrySector)pickedgeo;
						bool isceiling = (pickedsector is VisualCeiling); //mxd
						General.Interface.ShowSectorInfo(pickedsector.Level.sector, isceiling, !isceiling);
					}
					else
					{
						General.Interface.HideInfo();
					}
				} 
				// Thing picked?
				else if(target.picked is VisualThing) 
				{ 
					VisualThing pickedthing = (VisualThing)target.picked;
					General.Interface.ShowThingInfo(pickedthing.Thing);
				} 
				//mxd. Vertex picked?
				else if(target.picked is VisualVertex)
				{
					VisualVertex pickedvert = (VisualVertex)target.picked;
					General.Interface.ShowVertexInfo(pickedvert.Vertex);
				}
			}
			else
			{
				General.Interface.HideInfo();
			}
		}
		
		// This updates the VisualSectors and VisualThings that have their Changed property set
		internal void UpdateChangedObjects()
		{
			List<VisualSector> rebuiltsectors = new List<VisualSector>();
			bool geometrychanged = false;
			foreach(KeyValuePair<Sector, VisualSector> vs in allsectors)
			{
				if(vs.Value != null)
				{
					BaseVisualSector bvs = (BaseVisualSector)vs.Value;
					if(bvs.Changed)
					{
						bvs.Rebuild();
						rebuiltsectors.Add(bvs);
						geometrychanged = true;

						// Also update slope handles
						if (allslopehandles.ContainsKey(vs.Key))
							foreach (VisualSlope handle in allslopehandles[vs.Key])
								handle.Update();
					}
				}
			}

			foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
			{
				if(vt.Value != null)
				{
					BaseVisualThing bvt = (BaseVisualThing)vt.Value;
					if(bvt.Changed)
					{
						bvt.Rebuild();
						geometrychanged = true;
					}
				}
			}

			//mxd
			if(General.Map.UDMF && General.Map.Config.VertexHeightSupport)
			{
				foreach(KeyValuePair<Vertex, VisualVertexPair> pair in vertices)
					pair.Value.Update();
			}

			// Rebuild replaces geometry instances. Refresh only rebuilt sectors in the
			// current visible set so exposed missing-texture walls do not wait for the
			// next camera movement, and avoid a costly whole-frustum cull per wheel tick.
			RefreshVisibleSectorGeometry(rebuiltsectors);
			if(geometrychanged) InvalidatePickTarget(true);

			//mxd. Update event lines (still better than updating them on every frame redraw)
			if(General.Settings.GZShowEventLines)
				renderer.SetEventLines(General.Map.GetVisualEventLines(blockmap));
		}

		//mxd
		protected override void MoveSelectedThings(Vector2D direction, bool absoluteposition) 
		{
			List<VisualThing> visualthings = GetSelectedVisualThings(true);
			if(visualthings.Count == 0) return;

			PreAction(UndoGroup.ThingMove);

			Vector3D[] coords = new Vector3D[visualthings.Count];
			for(int i = 0; i < visualthings.Count; i++)
				coords[i] = visualthings[i].Thing.Position;

			//move things...
			Vector3D[] translatedcoords = TranslateCoordinates(coords, direction, absoluteposition);
			for(int i = 0; i < visualthings.Count; i++) 
			{
				BaseVisualThing t = (BaseVisualThing)visualthings[i];
				t.OnMove(translatedcoords[i]);
			}

			// Things may've changed sectors...
			FillBlockMap();

			PostAction();
		}

		//mxd
		private static Vector3D[] TranslateCoordinates(Vector3D[] coordinates, Vector2D direction, bool absolutePosition) 
		{
			if(coordinates.Length == 0) return null;

			Vector3D[] translatedCoords = new Vector3D[coordinates.Length];

			//move things...
			if(!absolutePosition) //...relatively (that's easy)
			{ 
				int camAngle = (int)Math.Round(Angle2D.RadToDeg(General.Map.VisualCamera.AngleXY));
				int sector = General.ClampAngle(camAngle - 45) / 90;
				direction = direction.GetRotated(sector * Angle2D.PIHALF);

				// When one of the values were zero before rotation the resulting value
				// will be really small but not zero, so we round them here
				direction.x = Math.Round(direction.x, 6);
				direction.y = Math.Round(direction.y, 6);

				for (int i = 0; i < coordinates.Length; i++)
					translatedCoords[i] = coordinates[i] + new Vector3D(direction);

				return translatedCoords;
			}

			//...to specified location preserving relative positioning (that's harder)
			if(coordinates.Length == 1) //just move it there
			{
				translatedCoords[0] = new Vector3D(direction.x, direction.y, coordinates[0].z);
				return translatedCoords;
			}

			//we need some reference
			double minX = coordinates[0].x;
			double maxX = minX;
			double minY = coordinates[0].y;
			double maxY = minY;

			//get bounding coordinates for selected things
			for(int i = 1; i < coordinates.Length; i++) 
			{
				if(coordinates[i].x < minX)
					minX = coordinates[i].x;
				else if(coordinates[i].x > maxX)
					maxX = coordinates[i].x;

				if(coordinates[i].y < minY)
					minY = coordinates[i].y;
				else if(coordinates[i].y > maxY)
					maxY = coordinates[i].y;
			}

			Vector2D selectionCenter = new Vector2D(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2);

			//move them
			for(int i = 0; i < coordinates.Length; i++)
				translatedCoords[i] = new Vector3D(Math.Round(direction.x - (selectionCenter.x - coordinates[i].x)), Math.Round(direction.y - (selectionCenter.y - coordinates[i].y)), Math.Round(coordinates[i].z));

			return translatedCoords;
		}

		//mxd
		public override void UpdateSelectionInfo() 
		{
			// Collect info
			int numWalls = 0;
			int numFloors = 0;
			int numCeilings = 0;
			int numThings = 0;
			int numVerts = 0;

			foreach(IVisualEventReceiver obj in selectedobjects) 
			{
				if(!obj.Selected) continue;

				if(obj is BaseVisualThing) numThings++;
				else if(obj is BaseVisualVertex) numVerts++;
				else if(obj is VisualCeiling) numCeilings++;
				else if(obj is VisualFloor)	numFloors++;
				else if(obj is VisualMiddleSingle || obj is VisualMiddleDouble || obj is VisualLower || obj is VisualUpper || obj is VisualMiddle3D || obj is VisualMiddleBack)
					numWalls++;
			}

			List<string> results = new List<string>();
			if(numWalls > 0) results.Add(numWalls + (numWalls > 1 ? " sidedefs" : " sidedef"));
			if(numFloors > 0) results.Add(numFloors + (numFloors > 1 ? " floors" : " floor"));
			if(numCeilings > 0) results.Add(numCeilings + (numCeilings > 1 ? " ceilings" : " ceiling"));
			if(numThings > 0) results.Add(numThings + (numThings > 1 ? " things" : " thing"));
			if(numVerts > 0) results.Add(numVerts + (numVerts > 1 ? " vertices" : " vertex"));

			// Display results
			string result = string.Empty;
			if(results.Count > 0) 
			{
				result = string.Join(", ", results.ToArray());
				int pos = result.LastIndexOf(",", StringComparison.Ordinal);
				if(pos != -1) result = result.Remove(pos, 1).Insert(pos, " and");
				result += " selected.";
			}

			General.Interface.DisplayStatus(StatusType.Selection, result);
		}

		//mxd
		internal void StartRealtimeInterfaceUpdate(SelectionType selectiontype)
		{
			switch(selectiontype)
			{
				case SelectionType.All:
				case SelectionType.Linedefs:
				case SelectionType.Sectors:
					General.Interface.OnEditFormValuesChanged += Interface_OnSectorEditFormValuesChanged;
					break;
				case SelectionType.Things:
					General.Interface.OnEditFormValuesChanged += Interface_OnThingEditFormValuesChanged;
					break;
				default:
					General.Interface.OnEditFormValuesChanged += Interface_OnEditFormValuesChanged;
					break;
			}
		}

		//mxd
		internal void StopRealtimeInterfaceUpdate(SelectionType selectiontype)
		{
			switch(selectiontype)
			{
				case SelectionType.All:
				case SelectionType.Linedefs:
				case SelectionType.Sectors:
					General.Interface.OnEditFormValuesChanged -= Interface_OnSectorEditFormValuesChanged;
					break;
				case SelectionType.Things:
					General.Interface.OnEditFormValuesChanged -= Interface_OnThingEditFormValuesChanged;
					break;
				default:
					General.Interface.OnEditFormValuesChanged -= Interface_OnEditFormValuesChanged;
					break;
			}
		}

		private List<VisualSidedefSlope> GetSlopeHandlePair()
		{
			List<VisualSidedefSlope> handles = GetSelectedSlopeHandles();

			// No handles selected, try to slope between highlighted handle and it smart pivot
			if (handles.Count == 0 && HighlightedTarget is VisualSidedefSlope)
			{
				//VisualSidedefSlope handle = VisualSidedefSlope.GetSmartPivotHandle((VisualSidedefSlope)HighlightedTarget, this);
				VisualSidedefSlope handle = (VisualSidedefSlope)((VisualSidedefSlope)HighlightedTarget).GetSmartPivotHandle();
				if (handle == null)
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Couldn't find a smart pivot handle.");
					return handles;
				}

				handles.Add((VisualSidedefSlope)HighlightedTarget);
				handles.Add(handle);
			}
			// One handle selected, try to slope between it and the highlighted handle or the selected one's smart pivot
			else if (handles.Count == 1)
			{
				if (HighlightedTarget == handles[0] || !(HighlightedTarget is VisualSidedefSlope))
				{
					VisualSidedefSlope handle;

					if (HighlightedTarget is VisualSidedefSlope)
						handle = (VisualSidedefSlope)((VisualSidedefSlope)HighlightedTarget).GetSmartPivotHandle();
					else
						handle = (VisualSidedefSlope)(handles[0].GetSmartPivotHandle());

					if (handle == null)
					{
						General.Interface.DisplayStatus(StatusType.Warning, "Couldn't find a smart pivot handle.");
						return handles;
					}

					handles.Add(handle);
				}
				else
				{
					handles.Add((VisualSidedefSlope)HighlightedTarget);
				}
			}
			// Return if more than two handles are selected
			else if (handles.Count > 2)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Too many slope handles selected.");
				return handles;
			}
			// Everything else
			else if (handles.Count != 2)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No slope handles selected or highlighted.");
				return handles;
			}

			return handles;
		}

		private IVisualEventReceiver GetObjectByMapElement3D(MapElement3D element)
		{
			if(element == null || element.IsDisposed)
				return null;
			else if (element is Floor3D floor)
				return VisualSectorExists(floor.Sector) ? GetBaseVisualSector(floor.Sector).Floor : null;
			else if (element is Ceiling3D ceiling)
				return VisualSectorExists(ceiling.Sector) ? GetBaseVisualSector(ceiling.Sector).Ceiling : null;
			else if (element is Lower3D lower)
				return VisualSectorExists(lower.Sidedef.Sector)
					? GetBaseVisualSector(lower.Sidedef.Sector).GetSidedefParts(lower.Sidedef).lower : null;
			else if (element is Upper3D upper)
				return VisualSectorExists(upper.Sidedef.Sector)
					? GetBaseVisualSector(upper.Sidedef.Sector).GetSidedefParts(upper.Sidedef).upper : null;
			else if (element is Middle3D middle)
			{
				if(!VisualSectorExists(middle.Sidedef.Sector)) return null;
				var parts = GetBaseVisualSector(middle.Sidedef.Sector).GetSidedefParts(middle.Sidedef);
				if (parts.middlesingle != null)
					return parts.middlesingle;
				else
					return parts.middledouble;
			}
			else if (element is Vertex3D vertex)
				return GetVisualVertex(vertex.Vertex, vertex.IsFloor);
			else if (element is Thing3D thing)
			{
				VisualThing visualthing;
				return allthings.TryGetValue(thing.Thing, out visualthing)
					? visualthing as BaseVisualThing : null;
			}
			else if (element is ThreeDFloorBottom3D threedfloorbottom)
			{
				if(!VisualSectorExists(threedfloorbottom.Sector)) return null;
				var vs = GetBaseVisualSector(threedfloorbottom.Sector);
				return vs.ExtraFloors.Find(f => f.ExtraFloor.Linedef == threedfloorbottom.ControlLinedef);
			}
			else if (element is ThreeDFloorTop3D threedfloortop)
			{
				if(!VisualSectorExists(threedfloortop.Sector)) return null;
				var vs = GetBaseVisualSector(threedfloortop.Sector);
				return vs.ExtraCeilings.Find(f => f.ExtraFloor.Linedef == threedfloortop.ControlLinedef);
			}
			else if (element is ThreeDFloorSide3D threedfloorside)
			{
				if(!VisualSectorExists(threedfloorside.Sidedef.Sector)) return null;
				var vs = GetBaseVisualSector(threedfloorside.Sidedef.Sector);
				var parts = vs.GetSidedefParts(threedfloorside.Sidedef).middle3d;
				return parts.Find(p => p.GetControlLinedef() == threedfloorside.ControlLinedef);
			}
			if (element is SidedefSlope3D sideslope)
			{
				List<VisualSlope> handles;
				if(!sidedefslopehandles.TryGetValue(sideslope.Sidedef.Sector, out handles)) return null;
				return handles
					?.Find(s => {
						var ss = s as VisualSidedefSlope;
		
						return
							ss.Sidedef == sideslope.Sidedef &&
							ss.Level.extrafloor?.Linedef == sideslope.ControlLinedef &&
							ss.Level.type == (sideslope.IsFloor ? SectorLevelType.Floor : SectorLevelType.Ceiling);
					}) as VisualSidedefSlope;
			}
			else if (element is VertexSlope3D vertexslope)
			{
				List<VisualSlope> handles;
				if(!vertexslopehandles.TryGetValue(vertexslope.Sector, out handles)) return null;
				return handles
					?.Find(s => {
						var vs = s as VisualVertexSlope;

						return
							vs.Vertex == vertexslope.Vertex &&
							vs.Level.extrafloor?.Linedef == vertexslope.ControlLinedef &&
						vs.Level.type == (vertexslope.IsFloor ? SectorLevelType.Floor : SectorLevelType.Ceiling);
					}) as VisualVertexSlope;
			}
			else
				return null;
		}

		#endregion

		#region ================== Extended Methods

		// This requests a sector's extra data
		internal SectorData GetSectorData(Sector s)
		{
			// Make fresh sector data when it doesn't exist yet
			if(!sectordata.ContainsKey(s))
				sectordata[s] = new SectorData(this, s);
			
			return sectordata[s];
		}

		//mxd. This requests a sector's extra data or null if given sector doesn't have it
		internal SectorData GetSectorDataEx(Sector s)
		{
			return (sectordata.ContainsKey(s) ? sectordata[s] : null);
		}

		// This requests a things's extra data
		internal ThingData GetThingData(Thing t)
		{
			// Make fresh sector data when it doesn't exist yet
			if(!thingdata.ContainsKey(t))
				thingdata[t] = new ThingData(this, t);
			
			return thingdata[t];
		}

		//mxd
		internal VertexData GetVertexData(Vertex v) 
		{
			if(!vertexdata.ContainsKey(v))
				vertexdata[v] = new VertexData(this, v);
			return vertexdata[v];
		}

		internal BaseVisualVertex GetVisualVertex(Vertex v, bool floor) 
		{
			if(!vertices.ContainsKey(v))
				vertices.Add(v, new VisualVertexPair(new BaseVisualVertex(this, v, false), new BaseVisualVertex(this, v, true)));

			return (floor ? (BaseVisualVertex)vertices[v].FloorVertex : (BaseVisualVertex)vertices[v].CeilingVertex);
		}

		//mxd
		internal void UpdateVertexHandle(Vertex v)
		{
			if(!vertices.ContainsKey(v))
				vertices.Add(v, new VisualVertexPair(new BaseVisualVertex(this, v, false), new BaseVisualVertex(this, v, true)));
			else
				vertices[v].Changed = true;
		}

		private sealed class SkyboxPreviewChoice
		{
			public readonly int Id;
			private readonly string text;

			public SkyboxPreviewChoice(int id, string text)
			{
				Id = id;
				this.text = text;
			}

			public override string ToString() { return text; }
		}

		private void EnsureVertexOffsetEffect(Sector s)
		{
			if(!General.Settings.EnhancedRenderingEffects || !General.Map.UDMF || sectordata == null
				|| s == null || s.IsDisposed || s.Sidedefs.Count != 3) return;
			if(vertexoffseteffectsectors.Add(s)) GetSectorData(s).AddEffectVertexOffset();
		}
		
		// This rebuilds the sector data
		// This requires that the blockmap is up-to-date!
		internal void RebuildElementData()
		{
			HashSet<Sector> effectsectors = null; //mxd
			List<Linedef>[] slopelinedefpass = new List<Linedef>[] { new List<Linedef>(), new List<Linedef>() };
			List<Thing>[] slopethingpass = new List<Thing>[] { new List<Thing>(), new List<Thing>() };
			List<VisualBlockEntry> vertexblocks = new List<VisualBlockEntry>(8);

			if (!General.Settings.EnhancedRenderingEffects || !General.Settings.SRB2RenderingEffects) //mxd
			{
				// Store all sectors with effects
				if (sectordata != null && sectordata.Count > 0)
					effectsectors = new HashSet<Sector>(sectordata.Keys);
			}
			
			if (!General.Settings.EnhancedRenderingEffects) //mxd
			{
				// Remove all vertex handles from selection
				if(vertices != null && vertices.Count > 0) 
				{
                    for (int i = 0; i < selectedobjects.Count; i++)
                    {
                        if (selectedobjects[i] is BaseVisualVertex)
                        {
                            RemoveSelectedObject(selectedobjects[i]);
                            i--;
                        }
                    }
				}
			}

			Dictionary<int, List<Sector>> sectortags = new Dictionary<int, List<Sector>>();
			Dictionary<int, List<Thing>> thingtags = new Dictionary<int, List<Thing>>();
			Dictionary<int, List<Linedef>> linetags = new Dictionary<int, List<Linedef>>();
			sectordata = new Dictionary<Sector, SectorData>(General.Map.Map.Sectors.Count);
			thingdata = new Dictionary<Thing, ThingData>(General.Map.Map.Things.Count);

			//mxd. Rebuild all sectors with effects
			if(effectsectors != null) 
			{
				foreach(Sector s in effectsectors)
				{
					if(!VisualSectorExists(s)) continue;

					// The visual sector associated is now outdated
					BaseVisualSector vs = GetBaseVisualSector(s);
					vs.UpdateSectorGeometry(true);
				}
			}

			if(General.Map.UDMF) 
			{
				vertexdata = new Dictionary<Vertex, VertexData>(General.Map.Map.Vertices.Count); //mxd
				vertices.Clear();
				vertexoffseteffectsectors.Clear();
			}

			if(!General.Settings.EnhancedRenderingEffects) return; //mxd
			
			// Find all sector who's tag is not 0 and hash them so that we can find them quickly
			foreach(Sector s in General.Map.Map.Sectors)
			{
				foreach(int tag in s.Tags)
				{
					if(tag == 0) continue;
					if(!sectortags.ContainsKey(tag)) sectortags[tag] = new List<Sector>();
					sectortags[tag].Add(s);
				}

				// ========== Thing vertex slope, vertices with UDMF vertex offsets ==========
				if (s.Sidedefs.Count == 3)
				{
					if(General.Map.UDMF)
					{
						bool hasvertexoffset = false;
						foreach(Sidedef sd in s.Sidedefs)
						{
							Vertex v = sd.IsFront ? sd.Line.End : sd.Line.Start;
							hasvertexoffset |= !double.IsNaN(v.ZFloor) || !double.IsNaN(v.ZCeiling);
						}

						// Existing offsets must participate in effect dependency building now.
						// Flat triangular sectors are initialized when first visible, avoiding
						// thousands of unused SectorData and visual-handle objects on entry.
						if(hasvertexoffset) EnsureVertexOffsetEffect(s);
					}
					List<Thing> slopeceilingthings = new List<Thing>(3);
					List<Thing> slopefloorthings = new List<Thing>(3);

					foreach (Sidedef sd in s.Sidedefs)
					{
						Vertex v = sd.IsFront ? sd.Line.End : sd.Line.Start;

						// Check if a thing is at this vertex
						blockmap.GetBlocks(v.Position, vertexblocks);
						foreach (VisualBlockEntry block in vertexblocks)
						{
							foreach (Thing t in block.Things)
							{
								if ((Vector2D)t.Position == v.Position)
								{
									switch (t.Type)
									{
										case 1504: slopefloorthings.Add(t); break;
										case 1505: slopeceilingthings.Add(t); break;
									}
								}
							}
						}
					}

					// Slope any floor vertices?
					if (slopefloorthings.Count > 0)
					{
						SectorData sd = GetSectorData(s);
						sd.AddEffectThingVertexSlope(slopefloorthings, true);
					}

					// Slope any ceiling vertices?
					if (slopeceilingthings.Count > 0)
					{
						SectorData sd = GetSectorData(s);
						sd.AddEffectThingVertexSlope(slopeceilingthings, false);
					}
				}
			}

			// Find interesting things (such as sector slopes)
			// Pass one of slope things, and determine which one are for pass two
			//TODO: unfuck this because UDB decided to overhaul this...
			foreach (Thing t in General.Map.Map.Things)
			{
				// SRB2
				if (t.Type == 750)
				{
					if (!thingtags.ContainsKey(t.Tag)) thingtags[t.Tag] = new List<Thing>();
					thingtags[t.Tag].Add(t);
				}
				continue;

				switch (t.Type)
				{
					// ========== Copy slope ==========
					case 9511:
					case 9510:
						slopethingpass[1].Add(t);
						break;

					// ========== Thing line slope ==========
					case 9501:
					case 9500:
						if (linetags.ContainsKey(t.Args[0]))
						{
							// Only slope each sector once, even when multiple lines of the same sector are tagged. See https://github.com/jewalky/UltimateDoomBuilder/issues/491
							List<Sector> slopedsectors = new List<Sector>();

							foreach (Linedef ld in linetags[t.Args[0]])
							{
								if (ld.Line.GetSideOfLine(t.Position) < 0.0f)
								{
									if (ld.Front != null && !slopedsectors.Contains(ld.Front.Sector))
									{
										GetSectorData(ld.Front.Sector).AddEffectThingLineSlope(t, ld.Front);
										slopedsectors.Add(ld.Front.Sector);
									}
								}
								else if (ld.Back != null && !slopedsectors.Contains(ld.Back.Sector))
								{
									GetSectorData(ld.Back.Sector).AddEffectThingLineSlope(t, ld.Back);
									slopedsectors.Add(ld.Back.Sector);
								}
							}
						}
						break;

					// ========== Thing slope ==========
					case 9503:
					case 9502:
						t.DetermineSector(blockmap);
						if (t.Sector != null)
						{
							SectorData sd = GetSectorData(t.Sector);
							sd.AddEffectThingSlope(t);
						}
						break;
				}
			}

			// Find interesting linedefs (such as line slopes)
			// This also determines which slope lines belong to pass one and pass two. See https://zdoom.org/wiki/Slope
			foreach (Linedef l in General.Map.Map.Linedefs)
			{
				// Builds a cache of linedef ids/tags. Used for slope things. Use linedef tags in UDMF
				if(General.Map.UDMF)
				{
					foreach(int tag in l.Tags)
					{
						if (!linetags.ContainsKey(tag)) linetags[tag] = new List<Linedef>();
						linetags[tag].Add(l);
					}
				}

				//mxd. Rewritten to use action ID instead of number
				if (l.Action == 0 || !General.Map.Config.LinedefActions.ContainsKey(l.Action)) continue;

				switch(General.Map.Config.LinedefActions[l.Action].Id.ToLowerInvariant())
				{
					// ========== Line Set Identification (121) (see https://zdoom.org/wiki/Line_SetIdentification) ==========
					// Builds a cache of linedef ids/tags. Used for slope things. Only used for Hexen format
					case "line_setidentification":
						int tag = l.Args[0] + l.Args[4] * 256;
						if (!linetags.ContainsKey(tag)) linetags[tag] = new List<Linedef>();
						linetags[tag].Add(l);
						break;

					// ========== Plane Align (181) (see http://zdoom.org/wiki/Plane_Align) ==========
					case "plane_align":
						slopelinedefpass[0].Add(l);
						break;

					// ========== Plane Copy (118) (mxd) (see http://zdoom.org/wiki/Plane_Copy) ==========
					case "plane_copy":
						slopelinedefpass[1].Add(l);
						break;

					case "srb2_vertexslope":
					{
						List<Thing> sourcethings = new List<Thing>();
						if (!thingtags.ContainsKey(l.Args[1]) || thingtags[l.Args[1]].Count == 0)
							break;
						foreach (Thing thing in thingtags[l.Args[1]])
						{
							if (sourcethings.Contains(thing))
								continue;
							sourcethings.Add(thing);
							break;
						}
						if (!thingtags.ContainsKey(l.Args[2]) || thingtags[l.Args[2]].Count == 0)
							break;
						foreach (Thing thing in thingtags[l.Args[2]])
						{
							if (sourcethings.Contains(thing))
								continue;
							sourcethings.Add(thing);
							break;
						}
						if (!thingtags.ContainsKey(l.Args[3]) || thingtags[l.Args[3]].Count == 0)
							break;
						foreach (Thing thing in thingtags[l.Args[3]])
						{
							if (sourcethings.Contains(thing))
								continue;
							sourcethings.Add(thing);
							break;
						}
						SectorData sd = GetSectorData((l.Args[0] < 2) ? l.Front.Sector : l.Back.Sector);
						sd.AddEffectSRB2ThingVertexSlope(sourcethings, (l.Args[0] & 1) != 1);
						break;
					}

					// ========== Sector 3D floor (160) (see http://zdoom.org/wiki/Sector_Set3dFloor) ==========
					case "sector_set3dfloor":
						if(l.Front != null)
						{
							//mxd. Added hi-tag/line ID check 
							int sectortag = (General.Map.UDMF || (l.Args[1] & (int)Effect3DFloor.FloorTypes.HiTagIsLineID) != 0) ? l.Args[0] : l.Args[0] + (l.Args[4] << 8);
							if(sectortags.ContainsKey(sectortag)) 
							{
								List<Sector> sectors = sectortags[sectortag];
								foreach(Sector s in sectors) 
								{
									SectorData sd = GetSectorData(s);
									sd.AddEffect3DFloor(l);
								}
							}
						}
						break;

					case "srb2_fofsolid":
					case "srb2_fofwater":
					case "srb2_fofsolidopaque":
					case "srb2_fofcrumbling":
					case "srb2_fofintangible":
					case "srb2_fofbustable":
					case "srb2_foflaser":
					case "srb2_fofcustom":
						if (l.Front != null && sectortags.ContainsKey(l.Args[0]))
						{
							List<Sector> sectors = sectortags[l.Args[0]];
							foreach (Sector s in sectors)
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffect3DFloor(l);
							}
						}
						break;

					case "srb2_foflight":
					case "srb2_foffog":
					case "srb2_fofintangibleinvisible":
						if (General.Settings.SRB2RenderingEffects && l.Front != null && sectortags.ContainsKey(l.Args[0]))
						{
							List<Sector> sectors = sectortags[l.Args[0]];
							foreach (Sector s in sectors)
							{
								SectorData sd = GetSectorData(s);
								//sd.AddEffectBrightnessLevel(l);
								sd.AddEffect3DFloor(l);
							}
						}
						break;

					// ========== Transfer Brightness (50) (see http://zdoom.org/wiki/ExtraFloor_LightOnly) =========
					case "extrafloor_lightonly":
						if(l.Front != null && sectortags.ContainsKey(l.Args[0]))
						{
							List<Sector> sectors = sectortags[l.Args[0]];
							foreach(Sector s in sectors) 
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffectBrightnessLevel(l);
							}
						}
						break;

					// ========== mxd. Transfer Floor Brightness (210) (see http://www.zdoom.org/w/index.php?title=Transfer_FloorLight) =========
					case "transfer_floorlight":
						if(l.Front != null && sectortags.ContainsKey(l.Args[0])) 
						{
							List<Sector> sectors = sectortags[l.Args[0]];
							foreach(Sector s in sectors) 
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffectTransferFloorBrightness(l);
							}
						}
						break;

					// ========== mxd. Transfer Ceiling Brightness (211) (see http://www.zdoom.org/w/index.php?title=Transfer_CeilingLight) =========
					case "transfer_ceilinglight":
						if(l.Front != null && sectortags.ContainsKey(l.Args[0])) 
						{
							List<Sector> sectors = sectortags[l.Args[0]];
							foreach(Sector s in sectors) 
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffectTransferCeilingBrightness(l);
							}
						}
						break;

					// ========== mxd. BOOM: Set Tagged Floor Lighting to Lighting on 1st Sidedef's Sector (213) =========
					case "boom_transfer_floorlight":
						if(l.Front != null && sectortags.ContainsKey(l.Tag))
						{
							List<Sector> sectors = sectortags[l.Tag];
							foreach(Sector s in sectors)
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffectTransferFloorBrightness(l);
							}
						}
						break;

					// ========== mxd. BOOM: Set Tagged Ceiling Lighting to Lighting on 1st Sidedef's Sector (261) =========
					case "boom_transfer_ceilinglight":
						if(l.Front != null && sectortags.ContainsKey(l.Tag))
						{
							List<Sector> sectors = sectortags[l.Tag];
							foreach(Sector s in sectors)
							{
								SectorData sd = GetSectorData(s);
								sd.AddEffectTransferCeilingBrightness(l);
							}
						}
						break;
				}
			}

			// Pass one for linedefs
			foreach (Linedef l in slopelinedefpass[0])
			{
				//mxd. Rewritten to use action ID instead of number
				if (l.Action == 0 || !General.Map.Config.LinedefActions.ContainsKey(l.Action)) continue;

				switch (General.Map.Config.LinedefActions[l.Action].Id.ToLowerInvariant())
				{
					// ========== Plane Align (181) (see http://zdoom.org/wiki/Plane_Align) ==========
					case "plane_align":
						if (((l.Args[0] == 1) || (l.Args[1] == 1)) && (l.Front != null))
						{
							SectorData sd = GetSectorData(l.Front.Sector);
							sd.AddEffectLineSlope(l);
						}
						if (((l.Args[0] == 2) || (l.Args[1] == 2)) && (l.Back != null))
						{
							SectorData sd = GetSectorData(l.Back.Sector);
							sd.AddEffectLineSlope(l);
						}
						break;
				}
			}

			// Pass two of slope things
			//TODO: unfuck this because UDB decided to overhaul this...
			foreach (Thing t in slopethingpass[1])
			{
				switch (t.Type)
				{
					// ========== Copy slope ==========
					case 9511:
					case 9510:
						t.DetermineSector(blockmap);
						if (t.Sector != null)
						{
							SectorData sd = GetSectorData(t.Sector);
							sd.AddEffectCopySlope(t);
						}
						break;
				}
			}
			

			// Pass two for linedefs
			foreach (Linedef l in slopelinedefpass[1])
			{
				if (l.Action == 0 || !General.Map.Config.LinedefActions.ContainsKey(l.Action)) continue;

				switch (General.Map.Config.LinedefActions[l.Action].Id.ToLowerInvariant())
				{
					// ========== Plane Copy (118) (mxd) (see http://zdoom.org/wiki/Plane_Copy) ==========
					case "plane_copy":
						{
							//check the flags...
							bool floorCopyToBack = false;
							bool floorCopyToFront = false;
							bool ceilingCopyToBack = false;
							bool ceilingCopyToFront = false;

							if (l.Args[4] > 0 && l.Args[4] != 3 && l.Args[4] != 12)
							{
								floorCopyToBack = (l.Args[4] & 1) == 1;
								floorCopyToFront = (l.Args[4] & 2) == 2;
								ceilingCopyToBack = (l.Args[4] & 4) == 4;
								ceilingCopyToFront = (l.Args[4] & 8) == 8;
							}

							// Copy slope to front sector
							if (l.Front != null)
							{
								if ((l.Args[0] > 0 || l.Args[1] > 0) || (l.Back != null && (floorCopyToFront || ceilingCopyToFront)))
								{
									SectorData sd = GetSectorData(l.Front.Sector);
									sd.AddEffectPlaneClopySlope(l, true);
								}
							}

							// Copy slope to back sector
							if (l.Back != null)
							{
								if ((l.Args[2] > 0 || l.Args[3] > 0) || (l.Front != null && (floorCopyToBack || ceilingCopyToBack)))
								{
									SectorData sd = GetSectorData(l.Back.Sector);
									sd.AddEffectPlaneClopySlope(l, false);
								}
							}
						}
						break;
				}
			}

			// Visual slope handles
			foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in allslopehandles)
			{
				foreach (VisualSlope handle in kvp.Value)
					if (handle != null && handle.Selected)
						if (handle is BaseVisualSlope)
							RemoveSelectedObject((BaseVisualSlope)handle);

				kvp.Value.Clear();
			}
			usedslopehandles.Clear();
			allslopehandles.Clear();
			sidedefslopehandles.Clear();
			vertexslopehandles.Clear();

			// Slope handles are numerous (two per surface/side, plus 3D floors),
			// but they are only pickable in an explicit slope-picking mode. Defer
			// building the all-map handle graph until the user requests that mode.
			slopehandlesdirty = General.Map.UDMF;
			if (pickingmode != PickingMode.Default) EnsureSlopeHandles();
		}

		private void EnsureSlopeHandles()
		{
			if (!slopehandlesdirty) return;

			if (General.Map.UDMF)
				BuildSlopeHandles(General.Map.Map.Sectors);

			slopehandlesdirty = false;
		}

		private void BuildSlopeHandles(IEnumerable<Sector> sectors)
		{
			if (!General.Map.UDMF)
				return;

			foreach (Sector s in sectors)
			{
				if (s.IsDisposed)
					continue;

				SectorData sectordata = GetSectorData(s);
				sectordata.Update();

				// Clear old data
				if (allslopehandles.ContainsKey(s)) allslopehandles.Remove(s);
				if (sidedefslopehandles.ContainsKey(s))	sidedefslopehandles.Remove(s);
				if (vertexslopehandles.ContainsKey(s)) vertexslopehandles.Remove(s);


				// Create visual sidedef slope handles
				foreach (Sidedef sidedef in s.Sidedefs)
				{
					// Create handles for the regular floor and ceiling
					CreateVisualSlopeHandle(sectordata.Floor, sidedef, true);
					CreateVisualSlopeHandle(sectordata.Ceiling, sidedef, false);

					// Create handles for 3D floors
					if (sectordata.ExtraFloors.Count > 0)
					{
						foreach (Effect3DFloor floor in sectordata.ExtraFloors)
						{
							CreateVisualSlopeHandle(floor.Floor, sidedef, false);
							CreateVisualSlopeHandle(floor.Ceiling, sidedef, true);
						}
					}
				}
			}

			// Create visual vertex slope handles
			foreach(Vertex v in General.Map.Map.Vertices)
			{
				if (v.IsDisposed || v.Linedefs.Count == 0)
					continue;

				HashSet<Sector> vertexsectors = new HashSet<Sector>();

				// Find all sectors that have lines connected to this vertex
				foreach(Linedef ld in v.Linedefs)
				{
					if (ld.IsDisposed)
						continue;

					if (ld.Front != null && ld.Front.Sector != null && !ld.Front.Sector.IsDisposed) vertexsectors.Add(ld.Front.Sector);
					if (ld.Back != null && ld.Back.Sector != null && !ld.Back.Sector.IsDisposed) vertexsectors.Add(ld.Back.Sector);
				}

				foreach(Sector s in vertexsectors)
				{
					SectorData sectordata = GetSectorData(s);
					sectordata.Update();

					// Create handles for the regular floor and ceiling
					CreateVisualSlopeHandle(sectordata.Floor, v, s, true);
					CreateVisualSlopeHandle(sectordata.Ceiling, v, s, false);

					// Create handles for 3D floors
					if (sectordata.ExtraFloors.Count > 0)
					{
						foreach (Effect3DFloor floor in sectordata.ExtraFloors)
						{
							CreateVisualSlopeHandle(floor.Floor, v, s, false);
							CreateVisualSlopeHandle(floor.Ceiling, v, s, true);
						}
					}
				}
			}
		}
		
		#endregion

		#region ================== Events

		// Help!
		public override void OnHelp()
		{
			General.ShowHelp("e_visual.html");
		}

		// When entering this mode
		public override void OnEngage()
		{
			Stopwatch engagewatch = Stopwatch.StartNew();

			// A BaseVisualMode instance is kept alive for quick round-trips to the
			// classic modes. Never carry its old ray hit (or a target lock left by an
			// interrupted edit) into the new Visual Mode session: geometry may have
			// been rebuilt while the camera position and direction stayed identical,
			// which otherwise leaves an unrelated surface permanently highlighted.
			locktarget = false;
			target = new VisualPickResult();
			highlighted = null;
			lasthighlighttype = null;
			haslastpickview = false;

			//mxd
			useSelectionFromClassicMode = BuilderPlug.Me.SyncSelection ? !General.Interface.ShiftState : General.Interface.ShiftState;
			if(useSelectionFromClassicMode)	UpdateSelectionInfo();

			// Read settings
			cameraflooroffset = General.Map.Config.ReadSetting("cameraflooroffset", cameraflooroffset);
			cameraceilingoffset = General.Map.Config.ReadSetting("cameraceilingoffset", cameraceilingoffset);

			// Fog colors and the spatial blockmap remain valid across an unchanged
			// round-trip through a classic mode. Avoid two full-map passes in that case.
			if(!BlockMapIsCurrent)
			{
				foreach(Sector s in General.Map.Map.Sectors)
					s.UpdateFogColor();
			}
			long fogready = engagewatch.ElapsedMilliseconds;

			// biwa. We need a blockmap for the slope things. Can't wait until it's built in base.OnEngage
			// This was the root cause for issue #160
			if(!BlockMapIsCurrent) FillBlockMap();
			long blockmapready = engagewatch.ElapsedMilliseconds;

			RebuildElementData();
			RefreshSkyboxObjects();
			long effectsready = engagewatch.ElapsedMilliseconds;

			// Objects are only selected when they are created, so for objects that are selected we have to make sure
			// that they are created immediately. Otherwise the selection order will not be correct, or the objects
			// will not be selected at all if they are out of the user's camera range when entering visual mode
			// See https://github.com/jewalky/UltimateDoomBuilder/issues/938
			if (useSelectionFromClassicMode)
			{
				foreach (Sector s in General.Map.Map.GetSelectedSectors(true))
				{
					BaseVisualSector bvs = CreateBaseVisualSector(s);
					bvs.Ceiling.PerformAutoSelection();
					bvs.Floor.PerformAutoSelection();
				}

				// Things are automatically selected on creation
				foreach (Thing t in General.Map.Map.GetSelectedThings(true))
					allthings[t] = CreateVisualThing(t);

				// For linedefs it's a bit more complicated...
				foreach (Linedef ld in General.Map.Map.GetSelectedLinedefs(true))
				{
					foreach (Sidedef sd in new Sidedef[] { ld.Front, ld.Back })
					{
						if (sd != null)
						{
							if (!allsectors.ContainsKey(sd.Sector))
								CreateBaseVisualSector(sd.Sector);

							BaseVisualSector selectedsector = (BaseVisualSector)allsectors[sd.Sector];
							selectedsector.PrepareSidedefGeometry(sd);
							VisualSidedefParts vsp = selectedsector.GetSidedefParts(sd);
							vsp.upper?.PerformAutoSelection();
							vsp.middlesingle?.PerformAutoSelection();
							vsp.middledouble?.PerformAutoSelection();
							vsp.lower?.PerformAutoSelection();

							if (vsp.middle3d != null)
								foreach (VisualMiddle3D vm in vsp.middle3d)
									vm.PerformAutoSelection();

							if (vsp.middleback != null)
								foreach (VisualMiddleBack vm in vsp.middleback)
									vm.PerformAutoSelection();
						}
					}
				}
			}

			//mxd. Update event lines
			renderer.SetEventLines(General.Map.GetVisualEventLines(blockmap));
			long eventlinesready = engagewatch.ElapsedMilliseconds;

			// [ZZ] this enables calling of this object from the outside world. Only after properly initialized pls.
			base.OnEngage();
			// haslastpickview deliberately remains false. The first processing pass
			// performs visibility culling before picking, so the fresh ray can also hit
			// geometry that was not retained from the previous mode.
			General.WriteLogLine("Visual Mode setup timings: fog " + fogready + " ms, blockmap "
				+ (blockmapready - fogready) + " ms, effects " + (effectsready - blockmapready)
				+ " ms, selection/event lines " + (eventlinesready - effectsready)
				+ " ms, final " + (engagewatch.ElapsedMilliseconds - eventlinesready)
				+ " ms, total " + engagewatch.ElapsedMilliseconds + " ms");
        }

		// When returning to another mode
		public override void OnDisengage()
		{
			HideSkyboxSelector();
			gravity = new Vector3D(0.0, 0.0, 0.0);
			base.OnDisengage();

			//mxd
			if(BuilderPlug.Me.SyncSelection ? !General.Interface.ShiftState : General.Interface.ShiftState)
			{
				//clear previously selected stuff
				General.Map.Map.ClearAllSelected();
				
				//refill selection
				List<int> selectedsectorindices = new List<int>();
				List<int> selectedlineindices = new List<int>();
				List<int> selectedvertexindices = new List<int>();

				foreach(IVisualEventReceiver obj in selectedobjects)
				{
					if(obj is BaseVisualThing) 
					{
						((BaseVisualThing)obj).Thing.Selected = true;
					}
					else if(obj is VisualFloor || obj is VisualCeiling) 
					{
						VisualGeometry vg = (VisualGeometry)obj;
						if(vg.Sector != null && vg.Sector.Sector != null && !selectedsectorindices.Contains(vg.Sector.Sector.Index))
						{
							selectedsectorindices.Add(vg.Sector.Sector.Index);
							vg.Sector.Sector.Selected = true;
						}
					}
					else if(obj is VisualLower || obj is VisualUpper || obj is VisualMiddleDouble 
						|| obj is VisualMiddleSingle || obj is VisualMiddle3D) 
					{
						VisualGeometry vg = (VisualGeometry)obj;
						if(vg.Sidedef != null && !selectedlineindices.Contains(vg.Sidedef.Line.Index))
						{
							selectedlineindices.Add(vg.Sidedef.Line.Index);
							vg.Sidedef.Line.Selected = true;
						}
					}
					else if(obj is VisualVertex)
					{
						VisualVertex v = (VisualVertex)obj;
						if(!selectedvertexindices.Contains(v.Vertex.Index))
						{
							selectedvertexindices.Add(v.Vertex.Index);
							v.Vertex.Selected = true;
						}
					}
				}
			}

			General.Map.Map.Update();

			// This Visual Mode instance may be cached for a fast unchanged round-trip.
			// Preserve any requested selection transfer to the 2D map above, but never
			// retain the old 3D surface selection inside the cached renderer objects.
			ClearSelection(true, true, true, true, true, false);
			SelectedGroup3D = null;

			// Do not retain references to visual objects that may be replaced while a
			// classic editing mode is active.
			locktarget = false;
			target = new VisualPickResult();
			highlighted = null;
			lasthighlighttype = null;
			haslastpickview = false;
		}
		
		// Processing
		public override void OnProcess(long deltatime)
		{
			long pickinterval = PICK_INTERVAL; // biwa
			// Process things?
			base.ProcessThings = (BuilderPlug.Me.ShowVisualThings != 0);
			
			// Setup the move multiplier depending on gravity
			Vector3D movemultiplier = new Vector3D(1.0, 1.0, 1.0);
			if(BuilderPlug.Me.UseGravity) movemultiplier.z = 0.0;
			General.Map.VisualCamera.MoveMultiplier = movemultiplier;
			
			// Apply gravity?
			if(BuilderPlug.Me.UseGravity && (General.Map.VisualCamera.Sector != null))
			{
				SectorData sd = GetSectorData(General.Map.VisualCamera.Sector);
				if(!sd.Updated) sd.Update();

				// Camera below floor level?
				Vector3D feetposition = General.Map.VisualCamera.Position;
				SectorLevel floorlevel = sd.GetFloorBelow(feetposition) ?? sd.Floor;
				double floorheight = floorlevel.plane.GetZ(General.Map.VisualCamera.Position);
				if(General.Map.VisualCamera.Position.z < (floorheight + cameraflooroffset + 0.1))
				{
					// Stay above floor
					gravity = new Vector3D(0.0, 0.0, 0.0);
					General.Map.VisualCamera.Position = new Vector3D(General.Map.VisualCamera.Position.x,
																	 General.Map.VisualCamera.Position.y,
																	 floorheight + cameraflooroffset);
				}
				else
				{
					// Fall down
					gravity.z += GRAVITY * General.Map.VisualCamera.Gravity * deltatime;
					if(gravity.z > 3.0) gravity.z = 3.0;

					// Test if we don't go through a floor
					if((General.Map.VisualCamera.Position.z + gravity.z) < (floorheight + cameraflooroffset + 0.1))
					{
						// Stay above floor
						gravity = new Vector3D(0.0, 0.0, 0.0);
						General.Map.VisualCamera.Position = new Vector3D(General.Map.VisualCamera.Position.x,
																		 General.Map.VisualCamera.Position.y,
																		 floorheight + cameraflooroffset);
					}
					else
					{
						// Apply gravity vector
						General.Map.VisualCamera.Position += gravity;
					}
				}

				// Camera above ceiling?
				feetposition = General.Map.VisualCamera.Position - new Vector3D(0, 0, cameraflooroffset - 7.0);
				SectorLevel ceillevel = sd.GetCeilingAbove(feetposition) ?? sd.Ceiling;
				double ceilheight = ceillevel.plane.GetZ(General.Map.VisualCamera.Position);
				if(General.Map.VisualCamera.Position.z > (ceilheight - cameraceilingoffset - 0.01))
				{
					// Stay below ceiling
					General.Map.VisualCamera.Position = new Vector3D(General.Map.VisualCamera.Position.x,
																	 General.Map.VisualCamera.Position.y,
																	 ceilheight - cameraceilingoffset);
				}
			}
			else
			{
				gravity = new Vector3D(0.0, 0.0, 0.0);
			}
			
			// Do processing
			base.OnProcess(deltatime);

			// A mouse-up can be lost when focus changes or another window opens while
			// selecting a surface. That used to leave locktarget set indefinitely, so
			// the same wall stayed highlighted until UZB was restarted. Keep the lock
			// only while one of the two actions which deliberately owns it is active.
			if(locktarget
				&& (Control.MouseButtons == MouseButtons.None
					|| (!General.Actions.CheckActionActive(General.ThisAssembly, "visualselect")
						&& !General.Actions.CheckActionActive(General.ThisAssembly, "textureselect"))))
			{
				locktarget = false;
				InvalidatePickTarget(true);
			}

			// Keep aim/highlight feedback ahead of deferred geometry maintenance. This
			// makes clicks respond immediately even while a large edit is still being
			// refreshed in small background batches.
			if (paintselectpressed)
				pickinterval = PICK_INTERVAL_PAINT_SELECT;
			Vector3D currentpickposition = General.Map.VisualCamera.Position;
			Vector3D currentpickdirection = PickingDir;
			bool pickviewchanged = !haslastpickview
				|| currentpickposition.x != lastpickposition.x
				|| currentpickposition.y != lastpickposition.y
				|| currentpickposition.z != lastpickposition.z
				|| currentpickdirection.x != lastpickdirection.x
				|| currentpickdirection.y != lastpickdirection.y
				|| currentpickdirection.z != lastpickdirection.z
				|| pickingmode != lastpickmode;
			if((paintselectpressed || pickviewchanged)
				&& Clock.CurrentTime > (lastpicktime + pickinterval))
			{
				PickTargetUnlocked();
				lastpicktime = Clock.CurrentTime;
			}

			// Refresh texture coordinates from alignment/undo in small batches. A
			// large connected wall chain must not monopolize the UI thread.
			ProcessPendingRetainedVisuals();
			ProcessPendingAlignedSidedefs();

			// Surface OnProcess only detects completed asynchronous texture loads. Skip
			// the complete visible-surface scan until an image actually finishes or the
			// cull produces a different visible set. The latter keeps surfaces which were
			// hidden when their image completed correct when they later enter the view.
			long processtime = Clock.CurrentTime;
			long texturecompletionversion = General.Map.Data.ImageLoadCompletionVersion;
			bool texturestatechanged = (texturecompletionversion != lasttexturecompletionversion)
				|| (visiblegeometryversion != lasttexturevisiblegeometryversion);
			if(texturestatechanged && processtime - lasttexturecompletionpolltime >= TEXTURE_COMPLETION_POLL_INTERVAL)
			{
				foreach(IVisualEventReceiver g in visiblegeometry)
					g.OnProcess(deltatime);
				lasttexturecompletionversion = texturecompletionversion;
				lasttexturevisiblegeometryversion = visiblegeometryversion;
				lasttexturecompletionpolltime = processtime;
			}
			// Locked-camera mouse processing is only needed while a gesture is active.
			// Calling the complete hover/drag path on every high-rate render tick while
			// idle repeated virtual dispatch and Help-panel checks for no visible change.
			if(!cursorunlocked)
			{
				MouseButtons buttons = General.Interface.MouseButtons;
				if(paintselectpressed || buttons != MouseButtons.None)
				{
					if(lockedmousemoveargs == null || lockedmousemovebuttons != buttons)
					{
						lockedmousemovebuttons = buttons;
						lockedmousemoveargs = new MouseEventArgs(buttons, 0, 0, 0, 0);
					}
					OnMouseMove(lockedmousemoveargs);
				}
			}
		}

		//mxd
		public override void OnClockReset()
		{
			base.OnClockReset();
			lastpicktime = 0;
			lasttexturecompletionpolltime = 0;
			haslastpickview = false;
		}

		// This draws a frame
		public override void OnRedrawDisplay()
		{
			renderer.SetClassicLightingColorMap(General.Map.Data.MainColorMap);

			// Start drawing
			if(renderer.Start())
			{
				// Use fog!
				renderer.SetFogMode(true);

				// Set target for highlighting
				renderer.ShowSelection = General.Settings.GZOldHighlightMode || General.Settings.UseHighlight; //mxd

				if(General.Settings.UseHighlight)
					renderer.SetHighlightedObject(target.picked);

				Vector3D skyboxposition;
				Vector3D skyboxtarget;
				bool renderskybox = TryGetSkyboxView(out skyboxposition, out skyboxtarget);
				bool normalselection = renderer.ShowSelection;
				bool normalhighlight = renderer.ShowHighlight;
				bool normalcages = renderer.DrawThingCages;

				if(renderskybox)
				{
					long currenttime = Clock.CurrentTime;
					Vector3D maincameraposition = General.Map.VisualCamera.Position;
					Vector3D maincameratarget = General.Map.VisualCamera.Target;
					bool cameramoved = !hasskyboxframe || maincameraposition != lastskyboxcameraposition
						|| maincameratarget != lastskyboxcameratarget;
					long skyboxframeage = currenttime - lastskyboxrendertime;
					// A time-limited moving refresh made the normal world update at the
					// selected frame rate while the projected skybox advanced in 16 ms
					// steps. At high FPS that looks like the skybox jitters against the map.
					// Render every changed camera view; retain the slower refresh only while
					// stationary so asynchronously loaded skybox textures still appear.
					bool updateskyboxframe = !hasskyboxframe || cameramoved
						|| skyboxframeage >= SKYBOX_IDLE_REFRESH_INTERVAL_MS;
					if(updateskyboxframe)
					{
						// Keep the skybox camera and its culling set on the same offscreen frame.
						// The completed target remains composited only while the view is still.
						renderer.PositionAndLookAt(skyboxposition, skyboxtarget);
						renderer.ApplyCurrentView();
						UpdateSkyboxVisibility(skyboxposition);
						renderer.SetSkyboxRenderPass(true);
						renderer.SkyboxBackground = false;
						renderer.ShowSelection = false;
						renderer.ShowHighlight = false;
						renderer.DrawThingCages = false;
						renderer.BeginSkyboxTarget();
						renderer.StartGeometry();
						foreach(VisualGeometry g in skyboxvisiblegeometry) renderer.AddSectorGeometry(g);
						if(BuilderPlug.Me.ShowVisualThings != 0)
							foreach(VisualThing t in skyboxvisiblethings) renderer.AddThingGeometry(t);
						renderer.FinishGeometry();
						lastskyboxrendertime = currenttime;
						lastskyboxcameraposition = maincameraposition;
						lastskyboxcameratarget = maincameratarget;
						hasskyboxframe = true;
					}

					renderer.PositionAndLookAt(General.Map.VisualCamera.Position, General.Map.VisualCamera.Target);
					renderer.CompositeSkyboxTarget();
					renderer.SetSkyboxRenderPass(false);
					renderer.SkyboxBackground = true;
					renderer.ShowSelection = normalselection;
					renderer.ShowHighlight = normalhighlight;
					renderer.DrawThingCages = normalcages;
				}
				else
				{
					renderer.PositionAndLookAt(General.Map.VisualCamera.Position, General.Map.VisualCamera.Target);
					renderer.ApplyCurrentView();
					renderer.SetSkyboxRenderPass(false);
					renderer.SkyboxBackground = false;
					renderer.ShowSelection = normalselection;
					renderer.ShowHighlight = normalhighlight;
					renderer.DrawThingCages = normalcages;
				}

				// Begin with normal-map geometry
				renderer.StartGeometry();

				// Render all visible sectors
				foreach(VisualGeometry g in visiblegeometry)
					renderer.AddSectorGeometry(g);

				if(BuilderPlug.Me.ShowVisualThings != 0)
				{
					// Render things in cages?
					renderer.DrawThingCages = ((BuilderPlug.Me.ShowVisualThings & 2) != 0);
					
					// Render all visible things
					foreach(VisualThing t in visiblethings)
						renderer.AddThingGeometry(t);
				}

				//mxd
				if(General.Map.UDMF && General.Map.Config.VertexHeightSupport && General.Settings.GZShowVisualVertices && vertices.Count > 0)
				{
					// Membership changes only when the culling result changes. Keep the
					// compact visible-handle list between redraws instead of rebuilding its
					// hash set on every high-rate camera frame.
					if(rendervisualverticesversion != visiblegeometryversion)
					{
						rendervisualvertices.Clear();
						rendervisualvertexset.Clear();
						foreach(Linedef ld in visiblelines)
						{
							VisualVertexPair pair;
							if(rendervisualvertexset.Add(ld.Start) && vertices.TryGetValue(ld.Start, out pair))
								rendervisualvertices.AddRange(pair.Vertices);
							if(rendervisualvertexset.Add(ld.End) && vertices.TryGetValue(ld.End, out pair))
								rendervisualvertices.AddRange(pair.Vertices);
						}
						rendervisualverticesversion = visiblegeometryversion;
					}

					renderer.SetVisualVertices(rendervisualvertices);
				}

				renderer.SetVisualSlopeHandles(usedslopehandles);

				// Done rendering geometry
				renderer.FinishGeometry();

				// Render crosshair
				renderer.RenderCrosshair();
				
				// Present!
				renderer.Finish();
			}
		}
		
		// After resources were reloaded
		protected override void ResourcesReloaded()
		{
			ClearPendingRetainedVisuals();
			pendingselectionrestore.Clear();
			base.ResourcesReloaded();
			PickTarget();
		}

		protected override void PrepareReloadResources()
		{
			base.PrepareReloadResources();
			Array.Clear(skyboxviewpointsectorpositionsvalid, 0, skyboxviewpointsectorpositionsvalid.Length);

			pendingalignedsidedefs.Clear();
			pendingalignedsidedefset.Clear();
			alignmentrefreshactive = false;

			// F8 may replace SOC-derived ThingTypeInfo instances while existing map
			// things still hold their previous cached size/height information. Refresh
			// those values before the new visual things are constructed.
			foreach(Thing thing in General.Map.Map.Things)
				if(!thing.IsDisposed) thing.UpdateConfiguration();

			RebuildElementData();
			RefreshSkyboxObjects();
			InvalidatePickTarget(false);
		}

		// Expand core undo/redo marks through the effect graph before visual sectors
		// are retained. This keeps control-sector slopes, 3D floors and thing/vertex
		// effects from leaving an unmarked dependent sector with stale geometry.
		protected override void ExpandPartialReloadMarks()
		{
			base.ExpandPartialReloadMarks();

			Queue<Sector> pending = new Queue<Sector>();
			Dictionary<Sector, bool> queued = new Dictionary<Sector, bool>();
			HashSet<Sector> processed = new HashSet<Sector>();
			HashSet<Sector> neighboursprocessed = new HashSet<Sector>();
			int addedmarks = 0;

			foreach(Sector sector in General.Map.Map.Sectors)
				if(sector.Marked) QueuePartialReloadSector(pending, queued, sector, true);

			if(sectordata != null)
			{
				foreach(KeyValuePair<Sector, SectorData> entry in sectordata)
				{
					if(entry.Key == null || (!entry.Key.IsDisposed && !entry.Key.Marked)) continue;
					foreach(KeyValuePair<Sector, bool> update in entry.Value.UpdateAlso)
						QueuePartialReloadSector(pending, queued, update.Key, update.Value);
				}
			}

			if(thingdata != null)
			{
				foreach(KeyValuePair<Thing, ThingData> entry in thingdata)
				{
					if(entry.Key == null || (!entry.Key.IsDisposed && !entry.Key.Marked)) continue;
					foreach(KeyValuePair<Sector, bool> update in entry.Value.UpdateAlso)
						QueuePartialReloadSector(pending, queued, update.Key, update.Value);
				}
			}

			if(vertexdata != null)
			{
				foreach(KeyValuePair<Vertex, VertexData> entry in vertexdata)
				{
					if(entry.Key == null || (!entry.Key.IsDisposed && !entry.Key.Marked)) continue;
					foreach(KeyValuePair<Sector, bool> update in entry.Value.UpdateAlso)
						QueuePartialReloadSector(pending, queued, update.Key, update.Value);
				}
			}

			while(pending.Count > 0)
			{
				Sector sector = pending.Dequeue();
				bool includeneighbours = queued[sector];
				queued.Remove(sector);
				if(sector == null || sector.IsDisposed) continue;

				if(!sector.Marked)
				{
					sector.Marked = true;
					addedmarks++;
				}

				if(includeneighbours && neighboursprocessed.Add(sector))
				{
					foreach(Sidedef side in sector.Sidedefs)
						if(side.Other != null) QueuePartialReloadSector(pending, queued, side.Other.Sector, false);
				}

				if(!processed.Add(sector) || sectordata == null) continue;
				SectorData data;
				if(!sectordata.TryGetValue(sector, out data)) continue;
				foreach(KeyValuePair<Sector, bool> update in data.UpdateAlso)
					QueuePartialReloadSector(pending, queued, update.Key, update.Value);
			}

			if(addedmarks > 0)
				General.WriteLogLine("Visual Mode undo/redo added " + addedmarks
					+ " effect-dependent sectors to the partial refresh");
		}

		private static void QueuePartialReloadSector(Queue<Sector> pending,
			Dictionary<Sector, bool> queued, Sector sector, bool includeneighbours)
		{
			if(sector == null || sector.IsDisposed) return;

			bool current;
			if(queued.TryGetValue(sector, out current))
			{
				if(includeneighbours && !current) queued[sector] = true;
			}
			else
			{
				queued.Add(sector, includeneighbours);
				pending.Enqueue(sector);
			}
		}

		protected override void PreparePartialReloadResources()
		{
			base.PreparePartialReloadResources();
			// The offscreen skybox image, controller lookup and visibility set describe
			// the pre-undo map. Refresh them before the immediate redo redraw, including
			// any viewpoint/centerpoint tags changed by the restored snapshot.
			RefreshSkyboxObjects();

			// A queued wall refresh from the state before an undo is obsolete. The map
			// values now contain the authoritative offsets and rebuilt walls will use them.
			pendingalignedsidedefs.Clear();
			pendingalignedsidedefset.Clear();
			alignmentrefreshactive = false;

			// This must run after FillBlockMap and before DoCulling. Recreating sectors
			// against the previous effect graph produced stretched planes and missing
			// floors until a later camera movement happened to rebuild them.
			RebuildElementData();
		}

		protected override bool RetainPartialReloadSector(Sector sector, VisualSector visualsector)
		{
			if(sector == null || sector.IsDisposed || !(visualsector is BaseVisualSector)) return false;

			if(pendingretainedsectorset.Add(sector))
				pendingretainedsectors.Enqueue(sector);
			return true;
		}

		protected override bool RetainPartialReloadThing(Thing thing, VisualThing visualthing)
		{
			if(thing == null || thing.IsDisposed || !(visualthing is BaseVisualThing)) return false;

			if(pendingretainedthingset.Add(thing))
				pendingretainedthings.Enqueue(thing);
			return true;
		}

		// This usually happens when geometry is changed by undo, redo, cut or paste actions
		// and uses the marks to check what needs to be reloaded.
		protected override void ResourcesReloadedPartial()
		{
			// Texture alignment changes only sidedef offsets/scales. The generic undo
			// path disposes marked sectors, rebuilds the spatial index and then rebuilds
			// every visible sector; that work is unnecessary and caused multi-second
			// pauses on large maps. Keep the existing geometry and refresh only touched
			// wall parts through the small per-frame alignment queue.
			List<Sidedef> markedtexturesides = null;
			List<Sector> markedtexturesectors = null;
			if(texturealignmentundoredo && !General.Map.UndoRedo.GeometryChanged
				&& !General.Map.UndoRedo.PopulationChanged
				&& ((markedtexturesides = General.Map.Map.GetMarkedSidedefs(true)).Count > 0
					|| (markedtexturesectors = General.Map.Map.GetMarkedSectors(true)).Count > 0))
			{
				if(markedtexturesectors == null)
					markedtexturesectors = General.Map.Map.GetMarkedSectors(true);

				// Sidedef property changes also mark their owning sectors. Exclude those
				// incidental marks so a large wall alignment does not recalculate every
				// floor and ceiling touching the aligned wall chain.
				HashSet<Sector> wallsectors = new HashSet<Sector>();
				foreach(Sidedef side in markedtexturesides)
				{
					if(side != null && side.Sector != null) wallsectors.Add(side.Sector);
				}

				List<Sector> markedflats = new List<Sector>();
				foreach(Sector sector in markedtexturesectors)
				{
					if(sector != null && !sector.IsDisposed && !wallsectors.Contains(sector))
						markedflats.Add(sector);
				}

				UpdateAlignedSidedefs(markedtexturesides, true);

				HashSet<Linedef> changedcontrollines = new HashSet<Linedef>();
				foreach(Sidedef side in markedtexturesides)
					if(side != null && !side.IsDisposed && side.Line != null && !side.Line.IsDisposed)
						changedcontrollines.Add(side.Line);

				List<VisualSector> rebuiltfofsectors = RebuildFOFSectorsForControlLines(changedcontrollines);
				if(rebuiltfofsectors.Count > 0)
				{
					RefreshVisibleSectorGeometry(rebuiltfofsectors);
					InvalidatePickTarget(true);
				}

				UpdateChangedFlatSurfaces(markedflats);
				PickTarget();
				if(rebuiltfofsectors.Count > 0) General.Interface.RedrawDisplay();
				return;
			}

			// Visual objects can be replaced by a partial rebuild even though their map
			// elements remain. Remember the stable map identities and reselect their new
			// visual counterparts after the synchronous or deferred refresh completes.
			foreach(IVisualEventReceiver selectedobject in selectedobjects)
			{
				if(selectedobject == null) continue;
				MapElement3D element = selectedobject.AsMapElement3D;
				if(element != null && !element.IsDisposed)
					pendingselectionrestore.Add(element);
			}

			// Let the core update the spatial index and retain changed live visuals.
			base.ResourcesReloadedPartial();

			// The core retained changed visual objects so their old geometry remains on
			// screen. Rebuild those objects in bounded batches between rendered frames;
			// running the legacy synchronous rebuild below would both defeat the frame
			// budget and briefly expose an empty replacement sector.
			bool retainedvisuals = pendingretainedsectors.Count > 0 || pendingretainedthings.Count > 0;
			if(retainedvisuals)
			{
				if(!retainedrefreshactive)
				{
					retainedrefreshactive = true;
					retainedrefreshstart = Clock.CurrentTime;
					retainedrefreshinitialsectors = pendingretainedsectors.Count;
					retainedrefreshinitialthings = pendingretainedthings.Count;
					General.WriteLogLine("Visual Mode retained refresh queued "
						+ retainedrefreshinitialsectors + " sectors and "
						+ retainedrefreshinitialthings + " things");
				}
			}

			if (General.Map.UndoRedo.GeometryChanged)
			{
				// The base doesn't know anything about slobe handles, so we have to clear them up ourself
				if (General.Map.UDMF)
				{
					List<Sector> removedsectors = new List<Sector>();

					// Get the sectors that were disposed...
					foreach(Sector s in allslopehandles.Keys)
					{
						if (s.IsDisposed)
							removedsectors.Add(s);
					}

					// ... so that we can remove their slope handles
					foreach(Sector s in removedsectors)
					{
						allslopehandles[s].Clear();
						allslopehandles.Remove(s);

						sidedefslopehandles[s].Clear();
						sidedefslopehandles.Remove(s);

						vertexslopehandles[s].Clear();
						vertexslopehandles.Remove(s);
					}

					// Rebuild changed handles only when the lazy handle graph has
					// actually been materialized.
					if (!slopehandlesdirty)
						BuildSlopeHandles(General.Map.Map.GetMarkedSectors(true));
				}

				if(retainedvisuals) return;
			}
			else
			{
				if(retainedvisuals) return;

				bool sectorsmarked = false;

				// Neighbour sectors must be updated as well
				foreach (Sector s in General.Map.Map.Sectors)
				{
					if(s.Marked)
					{
						sectorsmarked = true;
						foreach(Sidedef sd in s.Sidedefs)
						{
							sd.Marked = true;
							if(sd.Other != null) sd.Other.Marked = true;
						}
					}
				}
				
				// Go for all sidedefs to update
				foreach(Sidedef sd in General.Map.Map.Sidedefs)
				{
					if(sd.Marked && VisualSectorExists(sd.Sector))
					{
						BaseVisualSector vs = GetBaseVisualSector(sd.Sector);
						vs.PrepareSidedefGeometry(sd);
						VisualSidedefParts parts = vs.GetSidedefParts(sd);
						parts.SetupAllParts();
					}
				}
				
				// Go for all sectors to update
				foreach(Sector s in General.Map.Map.Sectors)
				{
					if(s.Marked)
					{
						SectorData sd = GetSectorDataEx(s);
						if(sd != null)
						{
							sd.Reset(false); //mxd (changed Reset implementation)

							// UpdateSectorGeometry for associated sectors (sd.UpdateAlso) as well!
							foreach(KeyValuePair<Sector, bool> us in sd.UpdateAlso)
							{
								if(VisualSectorExists(us.Key))
								{
									BaseVisualSector vs = GetBaseVisualSector(us.Key);
									vs.UpdateSectorGeometry(us.Value);
								}
							}
						}
						
						// And update for this sector ofcourse
						if(VisualSectorExists(s))
						{
							BaseVisualSector vs = GetBaseVisualSector(s);
							vs.UpdateSectorGeometry(false);
						}
					}
				}
				
				if(!sectorsmarked)
				{
					// No sectors or geometry changed. So we only have
					// to update things when they have changed.
					HashSet<Thing> toremove = new HashSet<Thing>(); //mxd
					foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
					{
						if((vt.Value != null) && vt.Key.Marked)
						{
							if(vt.Key.IsDisposed) toremove.Add(vt.Key); //mxd. Disposed things will cause problems
							else ((BaseVisualThing)vt.Value).Rebuild();
						}
					}

					//mxd. Remove disposed things
					foreach(Thing t in toremove)
					{
						if(allthings[t] != null) allthings[t].Dispose();
						allthings.Remove(t);
					}
				}
				else
				{
					// Rebuild only things located in one of the changed sectors. The core
					// partial reload already retained every unaffected visual thing.
					HashSet<Thing> toremove = new HashSet<Thing>();
					foreach(KeyValuePair<Thing, VisualThing> vt in allthings)
					{
						if(vt.Key == null || vt.Key.IsDisposed || vt.Key.Sector == null || vt.Key.Sector.Marked)
						{
							if(vt.Value != null) vt.Value.Dispose();
							toremove.Add(vt.Key);
						}
					}
					foreach(Thing thing in toremove) allthings.Remove(thing);
				}
				
				// Clear visibility collections
				visiblesectors.Clear();
				visibleblocks.Clear();
				visiblegeometry.Clear();
				visiblethings.Clear();
				
				// The core partial reload rebuilt effect dependencies before its first
				// culling pass. Apply any Changed flags raised above, then recull because
				// rebuilding a sector replaces the geometry objects in the visible list.
				UpdateChangedObjects();
				
				// Visibility culling (this re-creates the needed resources)
				DoCulling();
			}
			
			// Determine what we're aiming at now
			PickTarget();
			RestorePendingSelection();
		}
		
		// Mouse moves
		public override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			IVisualEventReceiver o = GetTargetEventReceiver(true);
			o.OnMouseMove(e);

			//mxd. Show hints!
			if(o.GetType() != lasthighlighttype) 
			{
				if(General.Interface.ActiveDockerTabName == "Help") 
				{
					if(o is BaseVisualGeometrySidedef) 
					{
						General.Hints.ShowHints(this.GetType(), "sidedefs");
					}
					else if(o is BaseVisualGeometrySector) 
					{
						General.Hints.ShowHints(this.GetType(), "sectors");
					}
					else if(o is BaseVisualThing) 
					{
						General.Hints.ShowHints(this.GetType(), "things");
					}
					else if(o is BaseVisualVertex) 
					{
						General.Hints.ShowHints(this.GetType(), "vertices");
					}
					else 
					{
						General.Hints.ShowHints(this.GetType(), HintsManager.GENERAL);
					}
				}

				lasthighlighttype = o.GetType();
			}

			// biwa
			if (o is NullVisualEventReceiver)
				highlighted = null;
			else if (o is VisualGeometry)
				highlighted = (VisualGeometry)o;
			else if (o is VisualThing)
				highlighted = (VisualThing)o;
		}
		
		private static bool IsTextureAlignmentSnapshot(UndoSnapshot snapshot)
		{
			if(snapshot == null || string.IsNullOrEmpty(snapshot.Description)) return false;
			return snapshot.Description.StartsWith("Auto-align", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Reset texture offsets", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Reset local texture offsets", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Reset FOF texture offsets", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Change texture offsets", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Change texture rotation", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Change texture scale", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Fit texture", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Paste texture", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Flood-fill ", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Change texture ", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Create middle texture", StringComparison.Ordinal)
				|| snapshot.Description.StartsWith("Delete texture", StringComparison.Ordinal);
		}

		public override bool OnUndoBegin()
		{
			texturealignmentundoredo = IsTextureAlignmentSnapshot(General.Map.UndoRedo.NextUndo);
			CaptureUndoRefreshPriority();
			return base.OnUndoBegin();
		}

		public override bool OnRedoBegin()
		{
			texturealignmentundoredo = IsTextureAlignmentSnapshot(General.Map.UndoRedo.NextRedo);
			CaptureUndoRefreshPriority();
			return base.OnRedoBegin();
		}

		private void CaptureUndoRefreshPriority()
		{
			VisualGeometry geometry = target.picked as VisualGeometry;
			undorefreshprioritysector = (geometry != null && geometry.Sector != null)
				? geometry.Sector.Sector : null;
		}

		private void FinishImmediateUndoRefresh()
		{
			bool rebuiltimmediate = false;
			Sector priority = undorefreshprioritysector;
			undorefreshprioritysector = null;

			// The surface under the cursor must never remain backed by the pre-undo
			// FOF graph. Rebuild it before the user can click again, even when a large
			// edit leaves the rest of the map on the frame-budgeted refresh queue.
			if(RebuildPendingRetainedSector(priority)) rebuiltimmediate = true;

			// The first post-undo frame should show the new state everywhere the camera
			// can currently see. Spend a modest one-frame budget on those sectors before
			// continuing the off-screen work through the retained refresh queue.
			if(RebuildVisiblePendingRetainedSectors() > 0) rebuiltimmediate = true;

			// Small undos are cheaper to finish now than to expose one or more frames
			// containing old textures. Large edits keep the existing bounded queue.
			if(pendingretainedsectorset.Count <= 6 && pendingretainedthingset.Count <= 32)
			{
				while(retainedrefreshactive) ProcessPendingRetainedVisuals();
			}

			if(pendingalignedsidedefset.Count <= 96)
			{
				while(pendingalignedsidedefs.Count > 0) ProcessPendingAlignedSidedefs();
			}

			if(rebuiltimmediate)
				DoCulling();

			PickTarget();
			General.Interface.RedrawDisplay();
		}

		private bool RebuildPendingRetainedSector(Sector sector)
		{
			if(sector == null || !pendingretainedsectorset.Remove(sector)) return false;
			if(sector.IsDisposed) return false;

			VisualSector visualsector;
			if(!allsectors.TryGetValue(sector, out visualsector) || !(visualsector is BaseVisualSector)) return false;
			((BaseVisualSector)visualsector).Rebuild();

			List<VisualSlope> handles;
			if(allslopehandles.TryGetValue(sector, out handles))
				foreach(VisualSlope handle in handles) handle.Update();

			return true;
		}

		private int RebuildVisiblePendingRetainedSectors()
		{
			if(pendingretainedsectors.Count == 0 || pendingretainedsectorset.Count == 0
				|| visiblesectors.Count == 0) return 0;

			HashSet<Sector> visibleset = new HashSet<Sector>();
			foreach(VisualSector visualsector in visiblesectors)
				if(visualsector != null && visualsector.Sector != null)
					visibleset.Add(visualsector.Sector);

			long start = Clock.CurrentTime;
			int rebuilt = 0;
			int queued = pendingretainedsectors.Count;
			for(int i = 0; i < queued; i++)
			{
				Sector sector = pendingretainedsectors.Dequeue();
				if(!pendingretainedsectorset.Contains(sector)) continue;

				bool withinbudget = rebuilt < UNDO_VISIBLE_REFRESH_MAX_SECTORS
					&& (rebuilt == 0 || Clock.CurrentTime - start < UNDO_VISIBLE_REFRESH_BUDGET_MS);
				if(withinbudget && visibleset.Contains(sector))
				{
					if(RebuildPendingRetainedSector(sector)) rebuilt++;
				}
				else
				{
					pendingretainedsectors.Enqueue(sector);
				}
			}

			return rebuilt;
		}

		// Undo performed
		public override void OnUndoEnd()
		{
			try
			{
				base.OnUndoEnd();
				FinishImmediateUndoRefresh();
				RebuildSelectedObjectsList();
			}
			finally
			{
				texturealignmentundoredo = false;
			}

			// We can't group with this undo level anymore
			lastundogroup = UndoGroup.None;
		}
		
		// Redo performed
		public override void OnRedoEnd()
		{
			try
			{
				base.OnRedoEnd();
				FinishImmediateUndoRefresh();
				RebuildSelectedObjectsList();
			}
			finally
			{
				texturealignmentundoredo = false;
			}
		}

		public override void OnScriptRunEnd()
		{
			base.OnScriptRunEnd();

			FillBlockMap();

			// Effects may've become invalid
			if (sectordata != null && sectordata.Count > 0) RebuildElementData();

			// As well as geometry...
			foreach (VisualSector sector in visiblesectors)
			{
				BaseVisualSector vs = (BaseVisualSector)sector;
				if (vs != null) vs.Rebuild();
			}

			RebuildSelectedObjectsList();
		}

		//mxd
		private void Interface_OnSectorEditFormValuesChanged(object sender, EventArgs e) 
		{
			if(allsectors == null) return;

			ILinedefEditForm linedefform = sender as ILinedefEditForm;
			if(linedefform != null && linedefform.InvisibleFOFPreviewAlphaChanged)
			{
				RefreshInvisibleFOFPreview(linedefform.EditedLinedefs);
				return;
			}

			ResetChangedVisualSurfaces();

			UpdateChangedObjects();
			ShowTargetInfo();
			General.Interface.RedrawDisplay();
		}

		//mxd
		private void Interface_OnThingEditFormValuesChanged(object sender, EventArgs e) 
		{
			//update visual sectors, which are affected by certain things
			List<Thing> things = GetSelectedThings();
			foreach(Thing t in things) 
			{
				if(thingdata.ContainsKey(t)) 
				{
					// Update what must be updated
					ThingData td = GetThingData(t);
					foreach(KeyValuePair<Sector, bool> s in td.UpdateAlso) 
					{
						if(VisualSectorExists(s.Key)) 
						{
							BaseVisualSector vs = GetBaseVisualSector(s.Key);
							vs.UpdateSectorGeometry(s.Value);
						}
					}
				}
			}
			
			UpdateChangedObjects();
			ShowTargetInfo();
			General.Interface.RedrawDisplay();
		}

		//mxd
		private void Interface_OnEditFormValuesChanged(object sender, EventArgs e) 
		{
			UpdateChangedObjects();
			ShowTargetInfo();
		}

		private void Interface_OnUpdateChangedObjects(object sender, EventArgs e)
		{
			UpdateChangedObjects();
		}

		//mxd
		private void SelectioninfoupdatetimerOnTick(object sender, EventArgs eventArgs) 
		{
			selectioninfoupdatetimer.Stop();
			UpdateSelectionInfo();
		}
		
		#endregion

		#region ================== Action Assist

		// Because some actions can only be called on a single (the targeted) object because
		// they show a dialog window or something, these functions help applying the result
		// to all compatible selected objects.
		
		// Apply texture offsets
		public void ApplyTextureOffsetChange(int dx, int dy)
		{
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, true, false, false, false);
			
			//mxd. Because Upper/Middle/Lower textures offsets should be threated separately in UDMF
			//MaxW. But they're not for Eternity, so this needs its own config setting
			if(General.Map.UDMF && General.Map.Config.UseLocalSidedefTextureOffsets)
			{
				HashSet<BaseVisualGeometrySidedef> donesides = new HashSet<BaseVisualGeometrySidedef>();
				HashSet<Sidedef> donefofcontrols = new HashSet<Sidedef>();
				foreach(IVisualEventReceiver i in objs) 
				{
					BaseVisualGeometrySidedef vs = (BaseVisualGeometrySidedef)i; //mxd
					VisualMiddle3D fofside = vs as VisualMiddle3D;
					if(fofside != null)
					{
						Linedef controlline = fofside.GetControlLinedef();
						Sidedef controlside = (controlline != null ? controlline.Front : null);
						if(controlside == null || !donefofcontrols.Add(controlside)) continue;
					}
					if(!donesides.Contains(vs)) 
					{
						//mxd. added scaling by texture scale
						if(vs.Texture.UsedInMap) //mxd. Otherwise it's MissingTexture3D and we probably don't want to drag that
							vs.OnChangeTextureOffset((int)(dx / vs.Texture.Scale.x), (int)(dy / vs.Texture.Scale.y), false);

						donesides.Add(vs);
					}
				}
			}
			else
			{
				HashSet<Sidedef> donesides = new HashSet<Sidedef>();
				foreach(IVisualEventReceiver i in objs) 
				{
					BaseVisualGeometrySidedef vs = (BaseVisualGeometrySidedef)i; //mxd
					if(!donesides.Contains(vs.Sidedef)) 
					{
						//mxd. added scaling by texture scale
						if(vs.Texture.UsedInMap) //mxd. Otherwise it's MissingTexture3D and we probably don't want to drag that
							vs.OnChangeTextureOffset((int)(dx / vs.Texture.Scale.x), (int)(dy / vs.Texture.Scale.y), false);

						donesides.Add(vs.Sidedef);
					}
				}
			}
		}

		// Apply flat offsets
		public void ApplyFlatOffsetChange(int dx, int dy)
		{
			HashSet<int> donesectors = new HashSet<int>();
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, false, false, false, false);
			foreach(IVisualEventReceiver i in objs)
			{
				BaseVisualGeometrySector bvs = (BaseVisualGeometrySector)i;
				if(bvs != null && !donesectors.Contains(bvs.Sector.Sector.Index))
				{
					//mxd. Sector surface belongs to 3d-floor?
					if(bvs.Level.sector.Index != bvs.Sector.Sector.Index)
					{
						// Don't update control sector several times
						if(!donesectors.Contains(bvs.Level.sector.Index))
						{
							// Update the offsets
							bvs.OnChangeTextureOffset(dx, dy, false);

							// Update control sector
							SectorData sd = GetSectorData(bvs.Level.sector);
							sd.Update();
							BaseVisualSector vs = GetBaseVisualSector(bvs.Level.sector);
							vs.Rebuild();

							// Add to collection
							donesectors.Add(bvs.Level.sector.Index);

							// Update 3d-floors
							List<Sector> updatealso = new List<Sector>(sd.UpdateAlso.Keys);
							foreach(Sector other in updatealso)
							{
								if(!donesectors.Contains(other.Index))
								{
									BaseVisualSector vsother = GetBaseVisualSector(other);
									vsother.Rebuild();

									// Add to collection
									donesectors.Add(other.Index);
								}
							}
						}
					}
					else
					{
						//mxd. Regular sector surface. Just update the offsets
						bvs.OnChangeTextureOffset(dx, dy, false);

						//mxd. Add to collection
						donesectors.Add(bvs.Sector.Sector.Index);
					}

					//mxd. Update sector geometry
					bvs.Sector.Rebuild();
				}
			}
		}

		// Apply upper unpegged flag
		public void ApplyUpperUnpegged(bool set)
		{
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, true, false, false, false);
			foreach(IVisualEventReceiver i in objs)
			{
				i.ApplyUpperUnpegged(set);
			}
		}

		// Apply lower unpegged flag
		public void ApplyLowerUnpegged(bool set)
		{
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, true, false, false, false);
			foreach(IVisualEventReceiver i in objs)
			{
				i.ApplyLowerUnpegged(set);
			}
		}

		// Apply specified flag
		public void ApplyLineFlag(string flag, string name, string explanation = null)
		{
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, true, false, false, false);
			HashSet<Linedef> lines = new HashSet<Linedef>();
			foreach(IVisualEventReceiver receiver in objs)
			{
				BaseVisualGeometrySidedef wall = receiver as BaseVisualGeometrySidedef;
				if(wall == null) continue;
				Linedef line = (wall.GeometryType == VisualGeometryType.WALL_MIDDLE_3D)
					? wall.GetControlLinedef() : wall.Sidedef.Line;
				if(line != null && !line.IsDisposed) lines.Add(line);
			}
			if(lines.Count == 0) return;

			CreateUndo("Change " + name + " flag");
			foreach(Linedef line in lines) line.SetFlag(flag, !line.IsFlagSet(flag));

			// Resolve every loaded sector affected by the line once. A control linedef
			// can produce many FOF walls, while a normal midtexture can exist on either
			// side of the line. Rebuilding the owning sectors after all flags are changed
			// is both cheaper and more reliable than updating individual wall objects:
			// those objects may be replaced by the flag change and leave the visible list
			// holding the old geometry until Visual Mode is re-entered.
			HashSet<Sidedef> refreshsides = new HashSet<Sidedef>();
			HashSet<Sector> refreshsectors = new HashSet<Sector>();
			foreach(Linedef line in lines)
			{
				if(line.Front != null && !line.Front.IsDisposed) refreshsides.Add(line.Front);
				if(line.Back != null && !line.Back.IsDisposed) refreshsides.Add(line.Back);
			}

			foreach(VisualSector visualsector in allsectors.Values)
			{
				BaseVisualSector sector = visualsector as BaseVisualSector;
				if(sector == null) continue;
				foreach(Sidedef side in sector.Sector.Sidedefs)
				{
					if(!sector.SidedefGeometryReady(side)) continue;
					foreach(VisualGeometry geometry in sector.GetSidedefGeometry(side))
					{
						VisualMiddle3D fofwall = geometry as VisualMiddle3D;
						if(fofwall != null && lines.Contains(fofwall.GetControlLinedef()))
						{
							refreshsides.Add(side);
							break;
						}
					}
				}
			}

			foreach(Sidedef side in refreshsides)
			{
				if(side != null && !side.IsDisposed && side.Sector != null && !side.Sector.IsDisposed)
					refreshsectors.Add(side.Sector);
			}

			foreach(Sector mapsector in refreshsectors)
			{
				SectorData data = GetSectorDataEx(mapsector);
				if(data != null) data.Reset(true);
				if(VisualSectorExists(mapsector)) GetBaseVisualSector(mapsector).Changed = true;
			}

			string result = "Changed " + name + " flag on " + lines.Count
				+ (lines.Count == 1 ? " linedef." : " linedefs.");
			if(!string.IsNullOrEmpty(explanation)) result += " " + explanation;
			SetActionResult(result);
		}

		// Apply texture change
		public void ApplySelectTexture(string texture, bool flat)
		{
			List<IVisualEventReceiver> objs;
			
			if(General.Map.Config.MixTexturesFlats)
			{
				// Apply on all compatible types
				objs = GetSelectedObjects(true, true, false, false, false);
			}
			else
			{
				// We don't want to mix textures and flats, so apply only on the appropriate type
				objs = GetSelectedObjects(flat, !flat, false, false, false);
			}
			
			foreach(IVisualEventReceiver i in objs)
			{
				i.ApplyTexture(texture);
			}
		}

        // This returns all selected objects
        internal List<IVisualEventReceiver> GetSelectedObjects(bool includesectors, bool includesidedefs, bool includethings, bool includevertices, bool includeslopehandles)
		{
			List<IVisualEventReceiver> objs = new List<IVisualEventReceiver>();
			foreach(IVisualEventReceiver i in selectedobjects)
			{
				if(includesectors && (i is BaseVisualGeometrySector)) objs.Add(i);
				else if(includesidedefs && (i is BaseVisualGeometrySidedef)) objs.Add(i);
				else if(includethings && (i is BaseVisualThing)) objs.Add(i);
				else if(includevertices && (i is BaseVisualVertex)) objs.Add(i); //mxd
				else if (includeslopehandles && (i is VisualSlope)) objs.Add(i); // biwa
			}

			// Add highlight?
			if(selectedobjects.Count == 0)
			{
				IVisualEventReceiver i = (target.picked as IVisualEventReceiver);
				if(includesectors && (i is BaseVisualGeometrySector)) objs.Add(i);
				else if(includesidedefs && (i is BaseVisualGeometrySidedef)) objs.Add(i);
				else if(includethings && (i is BaseVisualThing)) objs.Add(i);
				else if(includevertices && (i is BaseVisualVertex)) objs.Add(i); //mxd
				else if (includeslopehandles && (i is VisualSlope)) objs.Add(i); // biwa
			}

			return objs;
		}

		//mxd
		private static IEnumerable<IVisualEventReceiver> RemoveDuplicateSidedefs(IEnumerable<IVisualEventReceiver> objs) 
		{
			HashSet<Sidedef> processed = new HashSet<Sidedef>();
			HashSet<long> processedfofparts = new HashSet<long>();
			List<IVisualEventReceiver> result = new List<IVisualEventReceiver>();

			if(General.Map.UDMF)
			{
				// For UDMF maps, we only need to remove duplicate extrafloor sidedefs
				foreach(IVisualEventReceiver i in objs)
				{
					if(i is VisualMiddle3D)
					{
						VisualMiddle3D vm = i as VisualMiddle3D;
						Linedef controlline = vm.GetControlLinedef();
						Sidedef controlside = (controlline != null ? controlline.Front : null);
						if(vm.Sidedef == null || controlside == null) continue;

						// The same target sidedef may contain several independent FOF walls.
						// Remove duplicate visual copies of one layer, but keep layers backed
						// by different control sides so each receives its own scale/offset.
						long partkey = ((long)vm.Sidedef.Index << 32) | (uint)controlside.Index;
						if(processedfofparts.Add(partkey))
						{
							result.Add(i);
						}
					}
					else
					{
						result.Add(i);
					}
				}
			}
			else
			{
				// For Doom/Hexen maps, we need to remove all duplicates
				foreach(IVisualEventReceiver i in objs)
				{
					BaseVisualGeometrySidedef sidedef = i as BaseVisualGeometrySidedef;
					if(sidedef != null)
					{
						if(!processed.Contains(sidedef.Sidedef))
						{
							processed.Add(sidedef.Sidedef);
							result.Add(i);
						}
					}
					else
					{
						result.Add(i);
					}
				}
			}

			return result;
		}

		// This returns all selected sectors, no doubles
		public List<Sector> GetSelectedSectors()
		{
			HashSet<Sector> added = new HashSet<Sector>();
			List<Sector> sectors = new List<Sector>();
			foreach(IVisualEventReceiver i in selectedobjects)
			{
				BaseVisualGeometrySector sector = i as BaseVisualGeometrySector;
				if(sector != null && !added.Contains(sector.Level.sector))
				{
					sectors.Add(sector.Level.sector);
					added.Add(sector.Level.sector);
				}
			}

			// Add highlight?
			if((selectedobjects.Count == 0) && (target.picked is BaseVisualGeometrySector))
			{
				Sector s = ((BaseVisualGeometrySector)target.picked).Level.sector;
				if(!added.Contains(s)) sectors.Add(s);
			}
			
			return sectors;
		}

		/// <summary>
		/// Determines if the floor and/or ceiling of a sector is selected.
		/// </summary>
		/// <param name="sector">The sector to check</param>
		/// <param name="floor">If floor is selected or not</param>
		/// <param name="ceiling">If ceiling is selected or not</param>
		public void GetSelectedSurfaceTypesBySector(Sector sector, out bool floor, out bool ceiling)
		{
			floor = ceiling = false;

			foreach(IVisualEventReceiver i in selectedobjects)
			{
				if (i is VisualFloor && ((VisualFloor)i).Level.sector == sector)
					floor = true;
				else if (i is VisualCeiling && ((VisualCeiling)i).Level.sector == sector)
					ceiling = true;
			}
		}

		public void SetSelectedFloorBySector(Sector sector, bool selected)
		{
			if (selected)
			{
				var floor = GetBaseVisualSector(sector).Floor;
				floor.Selected = true;
				AddSelectedObject(floor);

				foreach (BaseVisualGeometrySector surface in GetVisualSurfacesByControlSector(sector, true, false, false))
				{
					surface.Selected = true;
					AddSelectedObject(surface);
				}
			}
			else
			{
				VisualFloor floor = null;
				foreach (IVisualEventReceiver i in selectedobjects)
					if (i is VisualFloor vf && ((VisualFloor)i).Level.sector == sector)
						floor = vf;

				if (floor != null)
				{
					floor.Selected = false;
					RemoveSelectedObject(floor);

					foreach (BaseVisualGeometrySector surface in GetVisualSurfacesByControlSector(sector, true, false, false))
					{
						surface.Selected = false;
						RemoveSelectedObject(surface);
					}
				}
			}
		}

		public void SetSelectedCeilingBySector(Sector sector, bool selected)
		{
			if (selected)
			{
				var ceiling = GetBaseVisualSector(sector).Ceiling;
				ceiling.Selected = true;
				AddSelectedObject(ceiling);

				foreach (BaseVisualGeometrySector surface in GetVisualSurfacesByControlSector(sector, false, true, false))
				{
					surface.Selected = true;
					AddSelectedObject(surface);
				}
			}
			else
			{
				VisualCeiling ceiling = null;
				foreach (IVisualEventReceiver i in selectedobjects)
					if (i is VisualCeiling vc && ((VisualCeiling)i).Level.sector == sector)
						ceiling = vc;

				if (ceiling != null)
				{
					ceiling.Selected = false;
					RemoveSelectedObject(ceiling);

					foreach (BaseVisualGeometrySector surface in GetVisualSurfacesByControlSector(sector, false, true, false))
					{
						surface.Selected = false;
						RemoveSelectedObject(surface);
					}
				}
			}
		}

		// This returns all selected linedefs, no doubles
		public List<Linedef> GetSelectedLinedefs()
		{
			HashSet<Linedef> added = new HashSet<Linedef>();
			List<Linedef> linedefs = new List<Linedef>();
			foreach(IVisualEventReceiver i in selectedobjects)
			{
				BaseVisualGeometrySidedef sidedef = i as BaseVisualGeometrySidedef;
				if(sidedef != null)
				{
					Linedef l = sidedef.GetControlLinedef(); //mxd
					if(!added.Contains(l))
					{
						linedefs.Add(l);
						added.Add(l);
					}
				}
			}

			// Add highlight?
			if((selectedobjects.Count == 0) && (target.picked is BaseVisualGeometrySidedef))
			{
				Linedef l = ((BaseVisualGeometrySidedef)target.picked).GetControlLinedef(); //mxd
				if(!added.Contains(l)) linedefs.Add(l);
			}

			return linedefs;
		}

		/// <summary>
		/// Determines if the upper/middle/lower parts of a sidedef are selected.
		/// </summary>
		/// <param name="sidedef">The sidedef tzo check</param>
		/// <param name="upper">If the upper part is selected</param>
		/// <param name="middle">If the middle part is selected</param>
		/// <param name="lower">If the lower part is selected</param>
		public void GetSelectedSurfaceTypesBySidedef(Sidedef sidedef, out bool upper, out bool middle, out bool lower)
		{
			upper = middle = lower = false;

			foreach(IVisualEventReceiver i in selectedobjects)
			{
				if (i is VisualUpper && ((VisualUpper)i).Sidedef == sidedef)
					upper = true;
				else if ((i is VisualMiddleSingle || i is VisualMiddleDouble || i is VisualMiddleBack) && ((BaseVisualGeometrySidedef)i).Sidedef == sidedef)
					middle = true;
				else if (i is VisualLower && ((VisualLower)i).Sidedef == sidedef)
					lower = true;
			}
		}

		public void SetSelectedUpperBySidedef(Sidedef sidedef, bool selected)
		{
		}

		public void SetSelectedMiddleBySidedef(Sidedef sidedef, bool selected)
		{
			if (selected)
			{
				var vs = GetBaseVisualSector(sidedef.Sector);
				vs.PrepareSidedefGeometry(sidedef);
				VisualSidedefParts parts = vs.GetSidedefParts(sidedef);

				if (parts.middlesingle != null)
				{
					parts.middlesingle.Selected = true;
					AddSelectedObject(parts.middlesingle);
				}
				else
				{
					parts.middledouble.Selected = true;
					AddSelectedObject(parts.middledouble);
				}
			}
			else
			{
				BaseVisualGeometrySidedef middle = null;
				foreach (IVisualEventReceiver i in selectedobjects)
					if ((i is VisualMiddleSingle || i is VisualMiddleDouble || i is VisualMiddle3D || i is VisualMiddleBack) && ((BaseVisualGeometrySidedef)i).Sidedef == sidedef)
						middle = (BaseVisualGeometrySidedef)i;

				if (middle != null)
				{
					middle.Selected = false;
					RemoveSelectedObject(middle);
				}
			}
		}

		public void SetSelectedLowerBySidedef(Sidedef sidedef, bool selected)
		{
		}

		// This returns all selected sidedefs, no doubles
		public List<Sidedef> GetSelectedSidedefs()
		{
			HashSet<Sidedef> added = new HashSet<Sidedef>();
			List<Sidedef> sidedefs = new List<Sidedef>();
			foreach(IVisualEventReceiver i in selectedobjects)
			{
				BaseVisualGeometrySidedef sidedef = i as BaseVisualGeometrySidedef;
				if(sidedef != null && !added.Contains(sidedef.Sidedef))
				{
					sidedefs.Add(sidedef.Sidedef);
					added.Add(sidedef.Sidedef);
				}
			}

			// Add highlight?
			/*
			if((selectedobjects.Count == 0) && (target.picked is BaseVisualGeometrySidedef))
			{
				Sidedef sd = ((BaseVisualGeometrySidedef)target.picked).Sidedef;
				if(!added.Contains(sd)) sidedefs.Add(sd);
			}
			*/

			return sidedefs;
		}

		// This returns all selected things, no doubles
		public List<Thing> GetSelectedThings()
		{
			HashSet<Thing> added = new HashSet<Thing>();
			List<Thing> things = new List<Thing>();
			foreach(IVisualEventReceiver i in selectedobjects)
			{
				BaseVisualThing thing = i as BaseVisualThing;
				if(thing != null && !added.Contains(thing.Thing))
				{
					things.Add(thing.Thing);
					added.Add(thing.Thing);
				}
			}

			// Add highlight?
			if((selectedobjects.Count == 0) && (target.picked is BaseVisualThing))
			{
				Thing t = ((BaseVisualThing)target.picked).Thing;
				if(!added.Contains(t)) things.Add(t);
			}

			return things;
		}

		//mxd. This returns all selected vertices, no doubles
		public List<Vertex> GetSelectedVertices() 
		{
			HashSet<Vertex> added = new HashSet<Vertex>();
			List<Vertex> verts = new List<Vertex>();

			foreach(IVisualEventReceiver i in selectedobjects)
			{
				BaseVisualVertex vertex = i as BaseVisualVertex;
				if(vertex != null && !added.Contains(vertex.Vertex)) 
				{
					verts.Add(vertex.Vertex);
					added.Add(vertex.Vertex);
				}
			}

			// Add highlight?
			if((selectedobjects.Count == 0) && (target.picked is BaseVisualVertex)) 
			{
				Vertex v = ((BaseVisualVertex)target.picked).Vertex;
				if(!added.Contains(v)) verts.Add(v);
			}

			return verts;
		}

		// This returns all selected slope handles, no doubles
		private List<VisualSidedefSlope> GetSelectedSlopeHandles()
		{
			HashSet<VisualSidedefSlope> added = new HashSet<VisualSidedefSlope>();
			List<VisualSidedefSlope> handles = new List<VisualSidedefSlope>();

			foreach(IVisualEventReceiver i in selectedobjects)
			{
				VisualSidedefSlope handle = i as VisualSidedefSlope;
				if(handle != null && !added.Contains(handle))
				{
					handles.Add(handle);
					added.Add(handle);
				}
			}

			// Add highlight?
			if((selectedobjects.Count == 0) && (target.picked is VisualSidedefSlope))
			{
				VisualSidedefSlope handle = (VisualSidedefSlope)target.picked;
				if (!added.Contains(handle)) handles.Add(handle);
			}

			return handles;
		}
		
		// This returns the IVisualEventReceiver on which the action must be performed
		private IVisualEventReceiver GetTargetEventReceiver(bool targetonly)
		{
			if(target.picked != null)
			{
				if(singleselection || target.picked.Selected || targetonly || target.picked is VisualSlope)
				{
					return (IVisualEventReceiver)target.picked;
				}

				if(selectedobjects.Count > 0)
				{
					return selectedobjects[0];
				}

				return (IVisualEventReceiver)target.picked;
			}

			return new NullVisualEventReceiver();
		}

		// Select the actual geometry under the crosshair. Sky-hack wall polygons
		// still represent an editable linedef and must not be redirected to the
		// neighbouring F_SKY1 ceiling or floor.
		private IVisualEventReceiver GetSelectionTargetEventReceiver()
		{
			return GetTargetEventReceiver(true);
		}

		//mxd. Copied from BuilderModes.ThingsMode
		// This creates a new thing
		private static Thing CreateThing(Vector2D pos) 
		{
			if(pos.x < General.Map.Config.LeftBoundary || pos.x > General.Map.Config.RightBoundary ||
				pos.y > General.Map.Config.TopBoundary || pos.y < General.Map.Config.BottomBoundary) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Failed to insert thing: outside of map boundaries.");
				return null;
			}

			// Create thing
			Thing t = General.Map.Map.CreateThing();
			if(t != null) 
			{
				General.Settings.ApplyDefaultThingSettings(t);
				t.Move(pos);
				t.UpdateConfiguration();
				General.Map.IsChanged = true;
				
				// Update things filter so that it includes this thing
				General.Map.ThingsFilter.Update();

				// Snap to grid enabled?
				if(General.Interface.SnapToGrid) 
				{
					// Snap to grid
					t.SnapToGrid();
				} 
				else 
				{
					// Snap to map format accuracy
					t.SnapToAccuracy();
				}
			}

			return t;
        }
		
		public List<Sector> GetControlLinedefTargetsSectors(Linedef linedef)
		{
			var targetSectors = new List<Sector>();

			if (linedef.Front == null)
				return targetSectors;

			foreach (Sector sector in General.Map.Map.Sectors)
				if (sector.Tags.Contains(linedef.Args[0]))
					targetSectors.Add(sector);

			return targetSectors;
		}

		public List<Sector> GetControlSectorTargetsSectors(Sector sector)
		{
			var targetSectors = new List<Sector>();

			foreach (Sidedef sidedef in sector.Sidedefs)
				targetSectors.AddRange(GetControlLinedefTargetsSectors(sidedef.Line));

			return targetSectors;
		}

		internal List<BaseVisualGeometrySector> GetVisualSurfacesByControlLinedef(Linedef linedef, bool floors = true, bool ceilings = true, bool backs = true)
		{
			var visualSurfaces = new List<BaseVisualGeometrySector>();

			foreach (Sector sector in GetControlLinedefTargetsSectors(linedef))
			{
				var vs = GetBaseVisualSector(sector);

				if (floors)
				{
					foreach (VisualFloor vf in vs.ExtraFloors)
						if (vf.GetControlSector() == sector)
							visualSurfaces.Add(vf);

					if (backs)
					{
						foreach (VisualFloor vf in vs.ExtraBackFloors)
							if (vf.GetControlSector() == sector)
								visualSurfaces.Add(vf);
					}
				}

				if (ceilings)
				{
					foreach (VisualCeiling vc in vs.ExtraCeilings)
						if (vc.GetControlSector() == sector)
							visualSurfaces.Add(vc);

					if (backs)
					{
						foreach (VisualCeiling vc in vs.ExtraBackCeilings)
							if (vc.GetControlSector() == sector)
								visualSurfaces.Add(vc);
					}
				}
			}

			return visualSurfaces;
		}

		internal List<BaseVisualGeometrySector> GetVisualSurfacesByControlSector(Sector sector, bool floors = true, bool ceilings = true, bool backs = true)
		{
			var visualFloors = new List<BaseVisualGeometrySector>();

			foreach (Sidedef sidedef in sector.Sidedefs)
				visualFloors.AddRange(GetVisualSurfacesByControlLinedef(sidedef.Line, floors, ceilings, backs));

			return visualFloors;
		}

		// Hack for UDBScript
		internal Dictionary<string, object> GetVisualObjectAsDictionary(object o)
		{
			var dict = new Dictionary<string, object>();

			if (o is VisualFloor && (o as VisualFloor).ExtraFloor == null)
			{
				var floor = o as VisualFloor;
				dict["type"] = "floor";
				dict["sector"] = floor.Sector.Sector;
			}
			else if (o is VisualCeiling && (o as VisualCeiling).ExtraFloor == null)
			{
				var ceiling = o as VisualCeiling;
				dict["type"] = "ceiling";
				dict["sector"] = ceiling.Sector.Sector;
			}
			else if (o is VisualCeiling)
			{
				var ceiling = o as VisualCeiling;
				dict["type"] = "3dfloor_top";
				dict["sector"] = ceiling.Sector.Sector;
				dict["controlLinedef"] = ceiling.ExtraFloor.Linedef;
			}
			else if (o is VisualMiddle3D || o is VisualMiddleBack)
			{
				var middle = o as VisualMiddle3D;
				dict["type"] = "3dfloor_side";
				dict["sidedef"] = middle.Sidedef;
				dict["sector"] = o is VisualMiddle3D ? middle.Sidedef.Other.Sector : middle.Sidedef.Sector;
				dict["controlLinedef"] = middle.ExtraFloor.Linedef;
			}
			else if (o is VisualFloor)
			{
				var floor = o as VisualFloor;
				dict["type"] = "3dfloor_bottom";
				dict["sector"] = floor.Sector.Sector;
				dict["controlLinedef"] = floor.ExtraFloor.Linedef;
			}
			else if (o is BaseVisualGeometrySidedef)
			{
				var middle = o as BaseVisualGeometrySidedef;
				dict["type"] = o is VisualUpper ? "side_upper" : o is VisualLower ? "side_lower" : "side_middle";
				dict["sidedef"] = middle.Sidedef;
			}

			return dict;
		}

		// Hack for UDBScript
		public List<Dictionary<string, object>> GetSelectedObjectsAsDictionary()
		{
			var list = new List<Dictionary<string, object>>();

			list.Select(o => GetVisualObjectAsDictionary(o));
			foreach (var o in selectedobjects)
				list.Add(GetVisualObjectAsDictionary(o));
			//list.(GetVisualObjectAsDictionary(o));

			return list;
		}

		// Hack for UDBScript
		public Dictionary<string, object> GetHighlightedObjectAsDictionary()
		{
			return GetVisualObjectAsDictionary(HighlightedTarget);
		}

		internal List<Group3D> FindGroups3DWithObject(IVisualEventReceiver o)
		{
			if (General.Map.Map.GroupsByMapElement3D.ContainsKey(o.AsMapElement3D))
			{
				var groups = General.Map.Map.GroupsByMapElement3D[o.AsMapElement3D];
				groups.RemoveAll(g => General.Map.Map.CleanupGroup3D(g));
				return groups;
			}
			else
			{
				return new List<Group3D>();
			}
		}

		public Group3D FindSelectedGroup3D()
		{
			return General.Map.Map.Groups3D
				.Find(group =>
					group.Count == selectedobjects.Count &&
						selectedobjects.All(o => group.Contains(o.AsMapElement3D))
				);
		}

		#endregion

		#region ================== Actions

        // [ZZ] I moved this out of ClearSelection because "cut selection" action needs this to only affect things.
        private void ClearSelection(bool clearsectors, bool clearsidedefs, bool clearthings, bool clearvertices, bool clearslopehandles, bool displaystatus)
        {
            selectedobjects.RemoveAll(obj =>
            {
                return ((obj is BaseVisualGeometrySector && clearsectors) ||
                        (obj is BaseVisualGeometrySidedef && clearsidedefs) ||
                        (obj is BaseVisualThing && clearthings) ||
                        (obj is BaseVisualVertex && clearvertices) ||
						(obj is VisualSlope && clearslopehandles));
            });

            //
            foreach (KeyValuePair<Sector, VisualSector> vs in allsectors)
            {
                if (vs.Value != null)
                {
                    BaseVisualSector bvs = (BaseVisualSector)vs.Value;
                    if (clearsectors)
                    {
                        if (bvs.Floor != null) bvs.Floor.Selected = false;
                        if (bvs.Ceiling != null) bvs.Ceiling.Selected = false;
                        foreach (VisualFloor vf in bvs.ExtraFloors) vf.Selected = false;
                        foreach (VisualCeiling vc in bvs.ExtraCeilings) vc.Selected = false;
                        foreach (VisualFloor vf in bvs.ExtraBackFloors) vf.Selected = false; //mxd
                        foreach (VisualCeiling vc in bvs.ExtraBackCeilings) vc.Selected = false; //mxd
                    }

                    if (clearsidedefs)
                    {
                        foreach (Sidedef sd in vs.Key.Sidedefs)
                        {
                            //mxd. VisualSidedefParts can contain references to visual geometry, which is not present in VisualSector.sidedefgeometry
                            bvs.GetSidedefParts(sd).DeselectAllParts();
                        }
                    }
                }
            }

            if (clearthings)
            {
                foreach (KeyValuePair<Thing, VisualThing> vt in allthings)
                {
                    if (vt.Value != null)
                    {
                        BaseVisualThing bvt = (BaseVisualThing)vt.Value;
                        bvt.Selected = false;
                    }
                }
            }

            //mxd
            if (clearvertices)
            {
                if (General.Map.UDMF)
                {
                    foreach (KeyValuePair<Vertex, VisualVertexPair> pair in vertices) pair.Value.Deselect();
                }
            }

			// biwa
			if (clearslopehandles)
			{
				if (General.Map.UDMF)
				{
					VisualSlope oldsmartpivot = null;

					foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in allslopehandles)
					{
						foreach (VisualSlope handle in kvp.Value)
						{
							// We want to keep the old smart pivot handle
							if (handle.SmartPivot)
								oldsmartpivot = handle;

							handle.Selected = false;
							handle.Pivot = false;
							handle.SmartPivot = false;
						}
					}

					usedslopehandles.Clear();

					// Clearing the used slopes also clears the currently highlighted handle and the smart pivot handle. For
					// performance reasons PickTarget() will not do its slope handle stuff if the current and new pick are
					// the same, so we need to re-add the currently picked slope handle and its smart pivot handle
					if (target.picked is VisualSlope)
					{
						usedslopehandles.Add((VisualSlope)target.picked);

						if (oldsmartpivot != null)
						{
							oldsmartpivot.SmartPivot = true;
							usedslopehandles.Add(oldsmartpivot);
						}
					}
				}
			}

			//mxd
			if (displaystatus)
            {
               General.Interface.DisplayStatus(StatusType.Selection, string.Empty);
            }
        }

        [BeginAction("clearselection", BaseAction = true)]
		public void ClearSelection()
		{
            ClearSelection(true, true, true, true, true, true);
			SelectedGroup3D = null;
		}

		[BeginAction("visualselect", BaseAction = true)]
		public void BeginSelect()
		{
			PreActionNoChange();
			PickTargetUnlocked();
			GetSelectionTargetEventReceiver().OnSelectBegin();
			PostAction(false, false);
		}

		[EndAction("visualselect", BaseAction = true)]
		public void EndSelect()
		{
			IVisualEventReceiver target = GetSelectionTargetEventReceiver();
			target.OnSelectEnd();

			//mxd
			bool rangeselected = false;
			if(General.Interface.ShiftState && General.Interface.CtrlState && target.Selected
				&& target is BaseVisualGeometrySidedef)
			{
				rangeselected = TrySelectTextureRange((BaseVisualGeometrySidedef)target);
			}

			if(!rangeselected && (General.Interface.ShiftState || General.Interface.CtrlState) && selectedobjects.Count > 0) 
			{
				if (General.Interface.AltState || !BuilderPlug.Me.UseBuggyFloodSelect)
				{
					target.SelectNeighbours(target.Selected, General.Interface.ShiftState, General.Interface.CtrlState, General.Interface.AltState);
				}
				else
				{
					IVisualEventReceiver[] selection = new IVisualEventReceiver[selectedobjects.Count];
					selectedobjects.CopyTo(selection);

					foreach (IVisualEventReceiver obj in selection)
						obj.SelectNeighbours(target.Selected, General.Interface.ShiftState, General.Interface.CtrlState, false);
				}
			}

			Renderer.ShowSelection = true;
			Renderer.ShowHighlight = true;
			PostAction(false, false);
		}

		// Ctrl+Shift-click selects the shortest connected run of matching wall
		// parts between the last selected endpoint and the clicked endpoint. This
		// is intentionally separate from Shift-click's texture flood selection.
		private bool TrySelectTextureRange(BaseVisualGeometrySidedef end)
		{
			BaseVisualGeometrySidedef start = null;
			if(end.Sidedef == null || end.Sidedef.IsDisposed || end.Sidedef.Sector == null)
				return true;

			string texture = end.GetTextureName();
			for(int i = selectedobjects.Count - 1; i >= 0; i--)
			{
				BaseVisualGeometrySidedef candidate = selectedobjects[i] as BaseVisualGeometrySidedef;
				if(candidate == null || candidate == end || candidate.Sidedef == null
					|| candidate.Sidedef.IsDisposed) continue;
				if(string.Equals(candidate.GetTextureName(), texture, StringComparison.OrdinalIgnoreCase))
				{
					start = candidate;
					break;
				}
			}

			// With only one endpoint, leave the clicked surface selected and wait for
			// the second Ctrl+Shift-click rather than flooding every matching texture.
			if(start == null)
			{
				SetActionResult("Wall range start selected. Ctrl+Shift-click the other connected wall face.");
				return true;
			}

			Queue<BaseVisualGeometrySidedef> pending = new Queue<BaseVisualGeometrySidedef>();
			Dictionary<BaseVisualGeometrySidedef, BaseVisualGeometrySidedef> previous
				= new Dictionary<BaseVisualGeometrySidedef, BaseVisualGeometrySidedef>();
			pending.Enqueue(start);
			previous.Add(start, null);

			while(pending.Count > 0 && !previous.ContainsKey(end))
			{
				BaseVisualGeometrySidedef current = pending.Dequeue();
				foreach(BaseVisualGeometrySidedef next in GetTextureRangeNeighbours(current, texture))
				{
					if(previous.ContainsKey(next)) continue;
					previous.Add(next, current);
					pending.Enqueue(next);
					if(next == end) break;
				}
			}

			if(!previous.ContainsKey(end))
			{
				SetActionResult("No connected matching-texture path was found between the selected surfaces.");
				return true;
			}

			int pathcount = 0;
			for(BaseVisualGeometrySidedef current = end; current != null; current = previous[current])
			{
				pathcount++;
				if(!current.Selected)
				{
					current.Selected = true;
					AddSelectedObject(current);
				}
			}

			SetActionResult("Selected matching texture range (" + pathcount + " surfaces).");
			return true;
		}

		private IEnumerable<BaseVisualGeometrySidedef> GetTextureRangeNeighbours(
			BaseVisualGeometrySidedef current, string texture)
		{
			if(current.Sidedef == null || current.Sidedef.IsDisposed || current.Sidedef.Line == null)
				yield break;

			Rectangle currentrect = BuilderModesTools.GetSidedefPartSize(current);
			if(currentrect.Width == 0 || currentrect.Height == 0) yield break;

			HashSet<BaseVisualGeometrySidedef> yielded = new HashSet<BaseVisualGeometrySidedef>();
			for(int direction = 0; direction < 2; direction++)
			{
				bool forward = (direction == 0);
				Vertex vertex;
				if(forward)
					vertex = current.Sidedef.IsFront ? current.Sidedef.Line.End : current.Sidedef.Line.Start;
				else
					vertex = current.Sidedef.IsFront ? current.Sidedef.Line.Start : current.Sidedef.Line.End;

				foreach(Linedef line in vertex.Linedefs)
				{
					if(line == null || line.IsDisposed || line == current.Sidedef.Line) continue;

					// Follow one oriented wall face around the shared vertex. This is the
					// same topology rule used by Visual Mode's neighbour selection: it can
					// continue through adjoining sectors, but never jumps through a
					// two-sided linedef to the wall on its opposite side.
					Sidedef side = null;
					if(forward)
					{
						if(line.Start == vertex) side = line.Front;
						else if(line.End == vertex) side = line.Back;
					}
					else
					{
						if(line.Start == vertex) side = line.Back;
						else if(line.End == vertex) side = line.Front;
					}

					if(side == null || side.IsDisposed || side.Sector == null || side.Sector.IsDisposed)
						continue;

					BaseVisualSector visualsector = GetBaseVisualSector(side.Sector);
					if(visualsector == null) continue;
					visualsector.PrepareSidedefGeometry(side);
					VisualSidedefParts parts = visualsector.GetSidedefParts(side);
					List<BaseVisualGeometrySidedef> candidates = new List<BaseVisualGeometrySidedef>(8);
					if(parts.lower != null) candidates.Add(parts.lower);
					if(parts.middlesingle != null) candidates.Add(parts.middlesingle);
					if(parts.middledouble != null) candidates.Add(parts.middledouble);
					if(parts.upper != null) candidates.Add(parts.upper);
					if(parts.middle3d != null) candidates.AddRange(parts.middle3d);
					if(parts.middleback != null) candidates.AddRange(parts.middleback);

					foreach(BaseVisualGeometrySidedef candidate in candidates)
					{
						if(candidate == null || candidate.Triangles < 1
							|| !string.Equals(candidate.GetTextureName(), texture, StringComparison.OrdinalIgnoreCase)) continue;

						Rectangle candidaterect = BuilderModesTools.GetSidedefPartSize(candidate);
						if(candidaterect.Width == 0 || candidaterect.Height == 0
							|| !candidaterect.IntersectsWith(currentrect)) continue;

						if(yielded.Add(candidate)) yield return candidate;
					}
				}
			}
		}

		[BeginAction("visualedit", BaseAction = true)]
		public void BeginEdit()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnEditBegin();
			PostAction();
		}

		[EndAction("visualedit", BaseAction = true)]
		public void EndEdit()
		{
			PreActionNoChange();
			GetTargetEventReceiver(false).OnEditEnd();
			PostAction();
		}

		[BeginAction("raisesector8")]
		public void RaiseSector8()
		{
			PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(8);
			PostAction(true, true, true);
		}

		[BeginAction("lowersector8")]
		public void LowerSector8()
		{
			PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(-8);
			PostAction(true, true, true);
		}

	    [BeginAction("raisesector1")]
	    public void RaiseSector1() {
	        PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(1);
			PostAction(true, true, true);
	    }

	    [BeginAction("lowersector1")]
	    public void LowerSector1() {
	        PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(-1);
			PostAction(true, true, true);
	    }

	    [BeginAction("raisesector128")]
	    public void RaiseSector128() {
	        PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(128);
			PostAction(true, true, true);
	    }

	    [BeginAction("lowersector128")]
	    public void LowerSector128() {
	        PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(-128);
			PostAction(true, true, true);
	    }

		[BeginAction("raisemapelementbygridsize")]
		public void RaiseMapElementByGridSize()
		{
			PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(General.Map.Grid.GridSize);
			PostAction(true, true, true);
		}

		[BeginAction("lowermapelementbygridsize")]
		public void LowerMapElementByGridSize()
		{
			PreAction(UndoGroup.SectorHeightChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, true);
			bool hasvisualslopehandles = objs.Any(o => o is VisualSlope);
			foreach (IVisualEventReceiver i in objs) // If slope handles are selected only apply the action to them
				if (!hasvisualslopehandles || (hasvisualslopehandles && i is VisualSlope))
					i.OnChangeTargetHeight(-General.Map.Grid.GridSize);
			PostAction(true, true, true);
		}


		//mxd
		[BeginAction("raisesectortonearest")]
		public void RaiseSectorToNearest() 
		{
			List<VisualSidedefSlope> selectedhandles = GetSelectedSlopeHandles();

			if (selectedhandles.Count > 0)
			{
				if (selectedhandles.Count > 1)
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Can only raise to nearest when one visual slope handle is selected");
					return;
				}

				int startheight = (int)Math.Round(selectedhandles[0].GetCenterPoint().z);
				int targetheight = int.MaxValue;

				foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in sidedefslopehandles)
				{
					foreach (VisualSidedefSlope handle in kvp.Value)
					{
						if (handle != selectedhandles[0] && handle.Sidedef.Line == selectedhandles[0].Sidedef.Line)
						{
							int z = (int)Math.Round(handle.GetCenterPoint().z);

							if (z > startheight && z < targetheight)
								targetheight = z;
						}
					}
				}

				if (targetheight != int.MaxValue)
				{
					PreAction(UndoGroup.SectorHeightChange);
					selectedhandles[0].OnChangeTargetHeight(targetheight - startheight);
					PostAction(true, true, true);
				}
				else
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Can't raise: already at the highest level");
				}
			}
			else
			{
				Dictionary<Sector, VisualFloor> floors = new Dictionary<Sector, VisualFloor>();
				Dictionary<Sector, VisualCeiling> ceilings = new Dictionary<Sector, VisualCeiling>();
				List<BaseVisualThing> things = new List<BaseVisualThing>();
				bool withinSelection = General.Interface.CtrlState;

				// Get selection
				if (selectedobjects.Count == 0)
				{
					if (target.picked is VisualFloor)
					{
						VisualFloor vf = (VisualFloor)target.picked;
						floors[vf.Level.sector] = vf;
					}
					else if (target.picked is VisualCeiling)
					{
						VisualCeiling vc = (VisualCeiling)target.picked;
						ceilings[vc.Level.sector] = vc;
					}
					else if (target.picked is BaseVisualThing)
					{
						things.Add((BaseVisualThing)target.picked);
					}
				}
				else
				{
					foreach (IVisualEventReceiver i in selectedobjects)
					{
						if (i is VisualFloor)
						{
							VisualFloor vf = (VisualFloor)i;
							floors[vf.Level.sector] = vf;
						}
						else if (i is VisualCeiling)
						{
							VisualCeiling vc = (VisualCeiling)i;
							ceilings[vc.Level.sector] = vc;
						}
						else if (i is BaseVisualThing)
						{
							things.Add((BaseVisualThing)i);
						}
					}
				}

				// Check what we have
				if (floors.Count + ceilings.Count == 0 && (things.Count == 0 || !General.Map.FormatInterface.HasThingHeight))
				{
					General.Interface.DisplayStatus(StatusType.Warning, "No suitable objects found!");
					return;
				}

				if (withinSelection)
				{
					string s = string.Empty;

					if (floors.Count == 1) s = "floors";

					if (ceilings.Count == 1)
					{
						if (!string.IsNullOrEmpty(s)) s += " and ";
						s += "ceilings";
					}

					if (!string.IsNullOrEmpty(s))
					{
						General.Interface.DisplayStatus(StatusType.Warning, "Can't do: at least 2 selected " + s + " are required!");
						return;
					}
				}

				// Process floors...
				int maxSelectedHeight = int.MinValue;
				int minSelectedCeilingHeight = int.MaxValue;
				int targetCeilingHeight = int.MaxValue;

				// Get highest ceiling height from selection
				foreach (KeyValuePair<Sector, VisualCeiling> group in ceilings)
				{
					if (group.Key.CeilHeight > maxSelectedHeight) maxSelectedHeight = group.Key.CeilHeight;
				}

				if (withinSelection)
				{
					// We are raising, so we don't need to check anything
					targetCeilingHeight = maxSelectedHeight;
				}
				else
				{
					// Get next higher floor or ceiling from surrounding unselected sectors
					foreach (Sector s in BuilderModesTools.GetSectorsAround(this, ceilings.Keys))
					{
						if (s.FloorHeight < targetCeilingHeight && s.FloorHeight > maxSelectedHeight)
							targetCeilingHeight = s.FloorHeight;
						else if (s.CeilHeight < targetCeilingHeight && s.CeilHeight > maxSelectedHeight)
							targetCeilingHeight = s.CeilHeight;
					}
				}

				// Ceilings...
				maxSelectedHeight = int.MinValue;
				int targetFloorHeight = int.MaxValue;

				// Get maximum floor and minimum ceiling heights from selection
				foreach (KeyValuePair<Sector, VisualFloor> group in floors)
				{
					if (group.Key.FloorHeight > maxSelectedHeight) maxSelectedHeight = group.Key.FloorHeight;
					if (group.Key.CeilHeight < minSelectedCeilingHeight) minSelectedCeilingHeight = group.Key.CeilHeight;
				}

				if (withinSelection)
				{
					// Check heights
					if (minSelectedCeilingHeight < maxSelectedHeight)
					{
						General.Interface.DisplayStatus(StatusType.Warning, "Can't do: lowest ceiling is lower than highest floor!");
						return;
					}
					targetFloorHeight = maxSelectedHeight;
				}
				else
				{
					// Get next higher floor or ceiling from surrounding unselected sectors
					foreach (Sector s in BuilderModesTools.GetSectorsAround(this, floors.Keys))
					{
						if (s.FloorHeight > maxSelectedHeight && s.FloorHeight < targetFloorHeight && s.FloorHeight <= minSelectedCeilingHeight)
							targetFloorHeight = s.FloorHeight;
						else if (s.CeilHeight > maxSelectedHeight && s.CeilHeight < targetFloorHeight && s.CeilHeight <= minSelectedCeilingHeight)
							targetFloorHeight = s.CeilHeight;
					}
				}

				//CHECK VALUES
				string alignFailDescription = string.Empty;

				if (floors.Count > 0 && targetFloorHeight == int.MaxValue)
				{
					// Raise to lowest ceiling?
					if (!withinSelection && minSelectedCeilingHeight > maxSelectedHeight)
					{
						targetFloorHeight = minSelectedCeilingHeight;
					}
					else
					{
						alignFailDescription = floors.Count > 1 ? "floors" : "floor";
					}
				}

				if (ceilings.Count > 0 && targetCeilingHeight == int.MaxValue)
				{
					if (!string.IsNullOrEmpty(alignFailDescription)) alignFailDescription += " and ";
					alignFailDescription += ceilings.Count > 1 ? "ceilings" : "ceiling";
				}

				if (!string.IsNullOrEmpty(alignFailDescription))
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Unable to align selected " + alignFailDescription + "!");
					return;
				}

				//APPLY VALUES
				PreAction(UndoGroup.SectorHeightChange);

				// Change floors heights
				if (floors.Count > 0)
				{
					foreach (KeyValuePair<Sector, VisualFloor> group in floors)
					{
						if (targetFloorHeight != group.Key.FloorHeight)
							group.Value.OnChangeTargetHeight(targetFloorHeight - group.Key.FloorHeight);
					}
				}

				// Change ceilings heights
				if (ceilings.Count > 0)
				{
					foreach (KeyValuePair<Sector, VisualCeiling> group in ceilings)
					{
						if (targetCeilingHeight != group.Key.CeilHeight)
							group.Value.OnChangeTargetHeight(targetCeilingHeight - group.Key.CeilHeight);
					}
				}

				// Change things heights. Align to higher 3d floor or actual ceiling.
				if (General.Map.FormatInterface.HasThingHeight)
				{
					foreach (BaseVisualThing vt in things)
					{
						if (vt.Thing.Sector == null) continue;
						SectorData sd = GetSectorData(vt.Thing.Sector);
						vt.OnMove(new Vector3D(vt.Thing.Position, BuilderModesTools.GetHigherThingZ(this, sd, vt)));
					}
				}

				PostAction(true, true, true);
			}
		}

		//mxd
		[BeginAction("lowersectortonearest")]
		public void LowerSectorToNearest() 
		{
			List<VisualSidedefSlope> selectedhandles = GetSelectedSlopeHandles();

			if (selectedhandles.Count > 0)
			{
				if (selectedhandles.Count > 1)
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Can only lower to nearest when one visual slope handle is selected");
					return;
				}

				int startheight = (int)Math.Round(selectedhandles[0].GetCenterPoint().z);
				int targetheight = int.MinValue;

				foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in sidedefslopehandles)
				{
					foreach (VisualSidedefSlope handle in kvp.Value)
					{
						if (handle != selectedhandles[0] && handle.Sidedef.Line == selectedhandles[0].Sidedef.Line)
						{
							int z = (int)Math.Round(handle.GetCenterPoint().z);

							if (z < startheight && z > targetheight)
								targetheight = z;
						}
					}
				}

				if (targetheight != int.MinValue)
				{
					PreAction(UndoGroup.SectorHeightChange);
					selectedhandles[0].OnChangeTargetHeight(-(startheight - targetheight));
					PostAction(true, true, true);
				}
				else
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Can't lower: already at the lowest level");
				}
			}
			else
			{
				Dictionary<Sector, VisualFloor> floors = new Dictionary<Sector, VisualFloor>();
				Dictionary<Sector, VisualCeiling> ceilings = new Dictionary<Sector, VisualCeiling>();
				List<BaseVisualThing> things = new List<BaseVisualThing>();
				bool withinSelection = General.Interface.CtrlState;

				// Get selection
				if (selectedobjects.Count == 0)
				{
					if (target.picked is VisualFloor)
					{
						VisualFloor vf = (VisualFloor)target.picked;
						floors[vf.Level.sector] = vf;
					}
					else if (target.picked is VisualCeiling)
					{
						VisualCeiling vc = (VisualCeiling)target.picked;
						ceilings[vc.Level.sector] = vc;
					}
					else if (target.picked is BaseVisualThing)
					{
						things.Add((BaseVisualThing)target.picked);
					}
				}
				else
				{
					foreach (IVisualEventReceiver i in selectedobjects)
					{
						if (i is VisualFloor)
						{
							VisualFloor vf = (VisualFloor)i;
							floors[vf.Level.sector] = vf;
						}
						else if (i is VisualCeiling)
						{
							VisualCeiling vc = (VisualCeiling)i;
							ceilings[vc.Level.sector] = vc;
						}
						else if (i is BaseVisualThing)
						{
							things.Add((BaseVisualThing)i);
						}
					}
				}

				// Check what we have
				if (floors.Count + ceilings.Count == 0 && (things.Count == 0 || !General.Map.FormatInterface.HasThingHeight))
				{
					General.Interface.DisplayStatus(StatusType.Warning, "No suitable objects found!");
					return;
				}

				if (withinSelection)
				{
					string s = string.Empty;

					if (floors.Count == 1) s = "floors";

					if (ceilings.Count == 1)
					{
						if (!string.IsNullOrEmpty(s)) s += " and ";
						s += "ceilings";
					}

					if (!string.IsNullOrEmpty(s))
					{
						General.Interface.DisplayStatus(StatusType.Warning, "Can't do: at least 2 selected " + s + " are required!");
						return;
					}
				}

				// Process floors...
				int minSelectedHeight = int.MaxValue;
				int targetFloorHeight = int.MinValue;

				// Get minimum floor height from selection
				foreach (KeyValuePair<Sector, VisualFloor> group in floors)
				{
					if (group.Key.FloorHeight < minSelectedHeight) minSelectedHeight = group.Key.FloorHeight;
				}

				if (withinSelection)
				{
					// We are lowering, so we don't need to check anything
					targetFloorHeight = minSelectedHeight;
				}
				else
				{
					// Get next lower ceiling or floor from surrounding unselected sectors
					foreach (Sector s in BuilderModesTools.GetSectorsAround(this, floors.Keys))
					{
						if (s.CeilHeight > targetFloorHeight && s.CeilHeight < minSelectedHeight)
							targetFloorHeight = s.CeilHeight;
						else if (s.FloorHeight > targetFloorHeight && s.FloorHeight < minSelectedHeight)
							targetFloorHeight = s.FloorHeight;
					}
				}

				// Ceilings...
				minSelectedHeight = int.MaxValue;
				int maxSelectedFloorHeight = int.MinValue;
				int targetCeilingHeight = int.MinValue;

				// Get minimum ceiling and maximum floor heights from selection
				foreach (KeyValuePair<Sector, VisualCeiling> group in ceilings)
				{
					if (group.Key.CeilHeight < minSelectedHeight) minSelectedHeight = group.Key.CeilHeight;
					if (group.Key.FloorHeight > maxSelectedFloorHeight) maxSelectedFloorHeight = group.Key.FloorHeight;
				}

				if (withinSelection)
				{
					if (minSelectedHeight < maxSelectedFloorHeight)
					{
						General.Interface.DisplayStatus(StatusType.Warning, "Can't do: lowest ceiling is lower than highest floor!");
						return;
					}
					targetCeilingHeight = minSelectedHeight;
				}
				else
				{
					// Get next lower ceiling or floor from surrounding unselected sectors
					foreach (Sector s in BuilderModesTools.GetSectorsAround(this, ceilings.Keys))
					{
						if (s.CeilHeight > targetCeilingHeight && s.CeilHeight < minSelectedHeight && s.CeilHeight >= maxSelectedFloorHeight)
							targetCeilingHeight = s.CeilHeight;
						else if (s.FloorHeight > targetCeilingHeight && s.FloorHeight < minSelectedHeight && s.FloorHeight >= maxSelectedFloorHeight)
							targetCeilingHeight = s.FloorHeight;
					}
				}

				//CHECK VALUES:
				string alignFailDescription = string.Empty;

				if (floors.Count > 0 && targetFloorHeight == int.MinValue)
					alignFailDescription = floors.Count > 1 ? "floors" : "floor";

				if (ceilings.Count > 0 && targetCeilingHeight == int.MinValue)
				{
					// Drop to highest floor?
					if (!withinSelection && maxSelectedFloorHeight < minSelectedHeight)
					{
						targetCeilingHeight = maxSelectedFloorHeight;
					}
					else
					{
						if (!string.IsNullOrEmpty(alignFailDescription)) alignFailDescription += " and ";
						alignFailDescription += ceilings.Count > 1 ? "ceilings" : "ceiling";
					}
				}

				if (!string.IsNullOrEmpty(alignFailDescription))
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Unable to align selected " + alignFailDescription + "!");
					return;
				}

				//APPLY VALUES:
				PreAction(UndoGroup.SectorHeightChange);

				// Change floor height
				if (floors.Count > 0)
				{
					foreach (KeyValuePair<Sector, VisualFloor> group in floors)
					{
						if (targetFloorHeight != group.Key.FloorHeight)
							group.Value.OnChangeTargetHeight(targetFloorHeight - group.Key.FloorHeight);
					}
				}

				// Change ceiling height
				if (ceilings.Count > 0)
				{
					foreach (KeyValuePair<Sector, VisualCeiling> group in ceilings)
					{
						if (targetCeilingHeight != group.Key.CeilHeight)
							group.Value.OnChangeTargetHeight(targetCeilingHeight - group.Key.CeilHeight);
					}
				}

				// Change things height. Drop to lower 3d floor or to actual sector's floor.
				if (General.Map.FormatInterface.HasThingHeight)
				{
					foreach (BaseVisualThing vt in things)
					{
						if (vt.Thing.Sector == null) continue;
						SectorData sd = GetSectorData(vt.Thing.Sector);
						vt.OnMove(new Vector3D(vt.Thing.Position, BuilderModesTools.GetLowerThingZ(this, sd, vt)));
					}
				}

				PostAction(true, true, true);
			}
		}

		//mxd
		[BeginAction("matchbrightness")]
		public void MatchBrightness() 
		{
			//check input
			if(!General.Map.UDMF) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "'Match Brightness' action works only in UDMF map format!");
				return;
			}

			if(selectedobjects.Count == 0) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "'Match Brightness' action requires a selection!");
				return;
			}

			IVisualEventReceiver highlighted = (IVisualEventReceiver)target.picked;

			if(highlighted is BaseVisualThing) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Highlight a surface, to which you want to match the brightness.");
				return;
			}

			//get target brightness
			int targetbrightness;
			if(highlighted is VisualFloor) 
			{
				VisualFloor v = (VisualFloor)highlighted;
				targetbrightness = v.Level.sector.Fields.GetValue("lightfloor", 0);
				if(!v.Level.sector.Fields.GetValue("lightfloorabsolute", false)) 
				{
					targetbrightness += v.Level.sector.Brightness;
				}
			} 
			else if(highlighted is VisualCeiling) 
			{
				VisualCeiling v = (VisualCeiling)highlighted;
				targetbrightness = v.Level.sector.Fields.GetValue("lightceiling", 0);
				if(!v.Level.sector.Fields.GetValue("lightceilingabsolute", false)) 
				{
					targetbrightness += v.Level.sector.Brightness;
				}
			} 
			else if(highlighted is VisualUpper || highlighted is VisualMiddleSingle || highlighted is VisualMiddleDouble || highlighted is VisualLower) 
			{
				BaseVisualGeometrySidedef v = (BaseVisualGeometrySidedef)highlighted;
				targetbrightness = v.Sidedef.Fields.GetValue("light", 0);
				if(!v.Sidedef.Fields.GetValue("lightabsolute", false)) 
				{
					targetbrightness += v.Sidedef.Sector.Brightness;
				}
			} 
			else if(highlighted is VisualMiddle3D) 
			{
				VisualMiddle3D v = (VisualMiddle3D)highlighted;
				Sidedef sd = v.GetControlLinedef().Front;
				if(sd == null) 
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Highlight a surface, to which you want to match the brightness.");
					return;
				}
				targetbrightness = sd.Fields.GetValue("light", 0);
				if(!sd.Fields.GetValue("lightabsolute", false)) 
				{
					targetbrightness += sd.Sector.Brightness;
				}

			} 
			else 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Highlight a surface, to which you want to match the brightness.");
				return;
			}

			//make undo
			CreateUndo("Match Brightness");
			targetbrightness = General.Clamp(targetbrightness, 0, 255);

			//apply new brightness
			foreach(IVisualEventReceiver obj in selectedobjects) 
			{
				if(obj == highlighted) continue;

				if(obj is VisualFloor) 
				{
					VisualFloor v = (VisualFloor)obj;
					v.Level.sector.Fields.BeforeFieldsChange();
					v.Sector.Changed = true;

					if(v.Level.sector.Fields.GetValue("lightfloorabsolute", false)) 
					{
						v.Level.sector.Fields["lightfloor"] = new UniValue(UniversalType.Integer, targetbrightness);
					} 
					else 
					{
						UniFields.SetInteger(v.Level.sector.Fields, "lightfloor", targetbrightness - v.Level.sector.Brightness, 0);
					}

					v.Sector.UpdateSectorGeometry(false);
				} 
				else if(obj is VisualCeiling) 
				{
					VisualCeiling v = (VisualCeiling)obj;
					v.Level.sector.Fields.BeforeFieldsChange();
					v.Sector.Changed = true;
					v.Sector.Sector.UpdateNeeded = true;

					if(v.Level.sector.Fields.GetValue("lightceilingabsolute", false)) 
					{
						v.Level.sector.Fields["lightceiling"] = new UniValue(UniversalType.Integer, targetbrightness);
					} 
					else 
					{
						UniFields.SetInteger(v.Level.sector.Fields, "lightceiling", targetbrightness - v.Level.sector.Brightness, 0);
					}

					v.Sector.UpdateSectorGeometry(false);
				} 
				else if(obj is VisualUpper || obj is VisualMiddleSingle || obj is VisualMiddleDouble || obj is VisualLower) 
				{
					BaseVisualGeometrySidedef v = (BaseVisualGeometrySidedef)obj;
					v.Sidedef.Fields.BeforeFieldsChange();
					v.Sector.Changed = true;

					if(v.Sidedef.Fields.GetValue("lightabsolute", false)) 
					{
						v.Sidedef.Fields["light"] = new UniValue(UniversalType.Integer, targetbrightness);
					} 
					else 
					{
						UniFields.SetInteger(v.Sidedef.Fields, "light", targetbrightness - v.Sidedef.Sector.Brightness, 0);
					}

					//Update 'lightfog' flag
					Tools.UpdateLightFogFlag(v.Sidedef);
				}
			}

			//Done
			General.Interface.DisplayStatus(StatusType.Action, "Matched brightness for " + selectedobjects.Count + " surfaces.");
			Interface_OnSectorEditFormValuesChanged(this, EventArgs.Empty);
		}

		[BeginAction("showvisualthings")]
		public void ShowVisualThings()
		{
			BuilderPlug.Me.ShowVisualThings++;
			if(BuilderPlug.Me.ShowVisualThings > 2) BuilderPlug.Me.ShowVisualThings = 0;

			string shortmessage = "Thing visibility is now " + (BuilderPlug.Me.ShowVisualThings > 0 ? (BuilderPlug.Me.ShowVisualThings > 1 ? "ON" : "SPRITE ONLY") : "OFF") + ".";
			string message = shortmessage;
			string key = Actions.Action.GetShortcutKeyDesc(General.Actions.Current.ShortcutKey);

			if (!string.IsNullOrEmpty(key))
				message += $" Press '{key}' to change.";

			General.ToastManager.ShowToast("showvisualthings", ToastType.INFO, "Changed thing visibility", message, new StatusInfo(StatusType.Action, shortmessage));
		}

		[BeginAction("raisebrightness8")]
		public void RaiseBrightness8()
		{
			bool local = General.Interface.AltState;
			PreAction(UndoGroup.SectorBrightnessChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, false, false, false);
			foreach(IVisualEventReceiver i in objs) i.OnChangeTargetBrightness(true, local);
			PostAction();
		}

		[BeginAction("lowerbrightness8")]
		public void LowerBrightness8()
		{
			bool local = General.Interface.AltState;
			PreAction(UndoGroup.SectorBrightnessChange);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, false, false, false);
			foreach(IVisualEventReceiver i in objs) i.OnChangeTargetBrightness(false, local);
			PostAction();
		}

		[BeginAction("movetextureleft")]	public void MoveTextureLeft1() { MoveTextureByOffset(-1, 0); }
		[BeginAction("movetextureright")]	public void MoveTextureRight1() { MoveTextureByOffset(1, 0); }
		[BeginAction("movetextureup")]		public void MoveTextureUp1() { MoveTextureByOffset(0, -1); }
		[BeginAction("movetexturedown")]	public void MoveTextureDown1() { MoveTextureByOffset(0, 1); }
		[BeginAction("movetextureleft8")]	public void MoveTextureLeft8() { MoveTextureByOffset(-8, 0); }
		[BeginAction("movetextureright8")]	public void MoveTextureRight8() { MoveTextureByOffset(8, 0); }
		[BeginAction("movetextureup8")]		public void MoveTextureUp8() { MoveTextureByOffset(0, -8); }
		[BeginAction("movetexturedown8")]	public void MoveTextureDown8() { MoveTextureByOffset(0, 8); }
		[BeginAction("movetextureleftgs")]	public void MoveTextureLeftGrid() { MoveTextureByOffset(-General.Map.Grid.GridSize, 0); }  //mxd
		[BeginAction("movetexturerightgs")]	public void MoveTextureRightGrid() { MoveTextureByOffset(General.Map.Grid.GridSize, 0); }  //mxd
		[BeginAction("movetextureupgs")]	public void MoveTextureUpGrid() { MoveTextureByOffset(0, -General.Map.Grid.GridSize); } //mxd
		[BeginAction("movetexturedowngs")]	public void MoveTextureDownGrid() { MoveTextureByOffset(0, General.Map.Grid.GridSize); } //mxd

		//mxd
		private void MoveTextureByOffset(int ox, int oy)
		{
			PreAction(UndoGroup.TextureOffsetChange);
			IEnumerable<IVisualEventReceiver> objs = RemoveDuplicateSidedefs(GetSelectedObjects(true, true, false, false, false));

			// FOF local X and Y offsets both live on the control side. Apply a
			// selected multi-wall FOF only once so the shared contribution is not
			// multiplied by the number of visible sides.
			HashSet<Sidedef> processed = new HashSet<Sidedef>();
			foreach (IVisualEventReceiver i in objs)
			{
				if (i is VisualMiddle3D)
				{
					VisualMiddle3D vm = i as VisualMiddle3D;
					Linedef controlline = vm.GetControlLinedef();
					Sidedef controlside = (controlline != null ? controlline.Front : null);
					if(controlside != null && processed.Add(controlside))
					{
						i.OnChangeTextureOffset(ox, oy, true);
					}
				}
				else
					i.OnChangeTextureOffset(ox, oy, true);
			}
			PostAction();
		}

		//mxd
		[BeginAction("scaleup")]	public void ScaleTextureUp() { ScaleTexture(1, 1); }
		[BeginAction("scaledown")]  public void ScaleTextureDown() { ScaleTexture(-1, -1); }
		[BeginAction("scaleupx")]   public void ScaleTextureUpX() { ScaleTexture(1, 0); }
		[BeginAction("scaledownx")] public void ScaleTextureDownX() { ScaleTexture(-1, 0); }
		[BeginAction("scaleupy")]   public void ScaleTextureUpY() { ScaleTexture(0, 1); } 
		[BeginAction("scaledowny")] public void ScaleTextureDownY() { ScaleTexture(0, -1); }
		//sphere
		[BeginAction("scalemultiply")] public void ScaleTextureMultiply() { ScaleTexture(2, 2); }
		[BeginAction("scaledivide")] public void ScaleTextureDivide() { ScaleTexture(-2, -2); }
		[BeginAction("invertscalex")] public void ScaleInvertX() { ScaleTexture(3, 0); }
		[BeginAction("invertscaley")] public void ScaleInvertY() { ScaleTexture(0, 3); }

		//mxd
		private void ScaleTexture(int incrementx, int incrementy)
		{
			PreAction(UndoGroup.TextureScaleChange);
			IEnumerable<IVisualEventReceiver> objs = RemoveDuplicateSidedefs(GetSelectedObjects(true, true, true, false, false));

			// sphere: Have to keep track of processed FOF sides, so they don't get scaled multiple times
			HashSet<Sidedef> processed = new HashSet<Sidedef>();
			foreach (IVisualEventReceiver i in objs)
			{
				if (i is VisualMiddle3D)
				{
					VisualMiddle3D vm = i as VisualMiddle3D;
					if (!processed.Contains(vm.ExtraFloor.Linedef.Front))
					{
						processed.Add(vm.ExtraFloor.Linedef.Front);
						i.OnChangeScale(incrementx, incrementy);
					}
				}
				else
					i.OnChangeScale(incrementx, incrementy);
			}

			PostAction();
		}

		[BeginAction("textureselect")]
		public void TextureSelect()
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			LockTarget();
			General.Interface.RedrawDisplay();
			GetTargetEventReceiver(false).OnSelectTexture();
			UnlockTarget();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		[BeginAction("texturecopy")]
		public void TextureCopy()
		{
			PreActionNoChange();
			IVisualEventReceiver i = GetTargetEventReceiver(true);
			i.OnCopyTexture(); //mxd
			if(!(i is VisualThing)) copybuffer.Clear(); //mxd. Not copying things any more...
			PostAction();
		}

		[BeginAction("texturepaste")]
		public void TexturePaste()
		{
			PreAction(UndoGroup.None);
			IEnumerable<IVisualEventReceiver> objs = RemoveDuplicateSidedefs(GetSelectedObjects(true, true, false, false, false));
			foreach(IVisualEventReceiver i in objs) i.OnPasteTexture();
			PostAction();
		}

		//mxd
		[BeginAction("visualautoalign")]
		public void TextureAutoAlign()
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();
			GetTargetEventReceiver(false).OnTextureAlign(true, true);
			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		[BeginAction("visualautoalignx")]
		public void TextureAutoAlignX()
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();
			GetTargetEventReceiver(false).OnTextureAlign(true, false);
			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		[BeginAction("visualautoaligny")]
		public void TextureAutoAlignY()
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();
			GetTargetEventReceiver(false).OnTextureAlign(false, true);
			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		//mxd
		[BeginAction("visualautoaligntoselection")]
		public void TextureAlignToSelected() 
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();

			AutoAlignTexturesToSelected(true, true);

			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		//mxd
		[BeginAction("visualautoaligntoselectionx")]
		public void TextureAlignToSelectedX() 
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();

			AutoAlignTexturesToSelected(true, false);

			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		//mxd
		[BeginAction("visualautoaligntoselectiony")]
		public void TextureAlignToSelectedY() 
		{
			PreAction(UndoGroup.None);
			renderer.SetCrosshairBusy(true);
			General.Interface.RedrawDisplay();

			AutoAlignTexturesToSelected(false, true);

			UpdateChangedObjects();
			renderer.SetCrosshairBusy(false);
			PostAction();
		}

		//mxd
		private void AutoAlignTexturesToSelected(bool alignX, bool alignY) 
		{
			string rest;
			if(alignX && alignY) rest = "(X and Y)";
			else if(alignX) rest = "(X)";
			else rest = "(Y)";

			CreateUndo("Auto-align textures to selected sidedefs " + rest);
			SetActionResult("Auto-aligned textures to selected sidedefs " + rest + ".");

			// Clear all marks, this will align everything it can
			General.Map.Map.ClearMarkedSidedefs(false);
			
			//get selection
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, true, false, false, false);

			// Align everything first, then refresh each changed sidedef once. The old
			// implementation scanned the complete map and rebuilt already-processed
			// sides after every selected starting wall.
			HashSet<Sidedef> changes = new HashSet<Sidedef>();
			foreach(IVisualEventReceiver i in objs)
			{
				BaseVisualGeometrySidedef side = (BaseVisualGeometrySidedef)i;
				
				// Make sure the texture is loaded (we need the texture size)
				if(!side.Texture.IsImageLoaded) side.Texture.LoadImageNow();

				//Align textures
				changes.UnionWith(AutoAlignTextures(side, side.Texture, alignX, alignY, false, false));
			}

			UpdateAlignedSidedefs(changes, true);
		}

		//mxd
		[BeginAction("visualfittextures")]
		private void FitTextures() 
		{
			PreAction(UndoGroup.None);
			
			// Get selection
			IEnumerable<IVisualEventReceiver> objs = RemoveDuplicateSidedefs(GetSelectedObjects(false, true, false, false, false));
			List<BaseVisualGeometrySidedef> sides = new List<BaseVisualGeometrySidedef>();
			foreach(IVisualEventReceiver i in objs)
			{
				BaseVisualGeometrySidedef side = (BaseVisualGeometrySidedef)i;
				if(side != null) sides.Add(side);
			}

			if(sides.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Fit Textures requires a selected or highlighted wall surface.");
				PostAction();
				return;
			}

			// Show form
			FitTexturesForm form = new FitTexturesForm();

			// Undo changes?
			if(form.Setup(sides) && form.ShowDialog((Form)General.Interface) == DialogResult.Cancel)
				General.Map.UndoRedo.WithdrawUndo();

			PostAction();
		}

		// Exact one-repeat fit for the common case: make each selected/highlighted
		// wall texture match that surface's width and height without opening a dialog.
		[BeginAction("visualautofittexture")]
		private void AutoFitTexture()
		{
			PreAction(UndoGroup.None);

			IEnumerable<IVisualEventReceiver> objs = RemoveDuplicateSidedefs(GetSelectedObjects(false, true, false, false, false));
			List<BaseVisualGeometrySidedef> sides = new List<BaseVisualGeometrySidedef>();
			foreach(IVisualEventReceiver obj in objs)
			{
				BaseVisualGeometrySidedef side = obj as BaseVisualGeometrySidedef;
				if(side != null) sides.Add(side);
			}

			List<SortedVisualSide> sorted = BuilderModesTools.SortVisualSides(sides);
			if(sorted.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Auto-fit Texture requires a visible selected or highlighted wall surface.");
				PostAction();
				return;
			}

			FitTextureOptions options = new FitTextureOptions
			{
				FitWidth = true,
				FitHeight = true,
				FitAcrossSurfaces = false,
				AutoWidth = false,
				AutoHeight = false,
				HorizontalRepeat = 1.0,
				VerticalRepeat = 1.0
			};

			foreach(SortedVisualSide side in sorted) side.OnTextureFit(options);
			SetActionResult("Auto-fitted " + sorted.Count + " wall texture" + (sorted.Count == 1 ? "." : "s."));
			PostAction();
		}

		[BeginAction("toggleupperunpegged")]
		public void ToggleUpperUnpegged()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnToggleUpperUnpegged();
			PostAction();
		}

		[BeginAction("togglelowerunpegged")]
		public void ToggleLowerUnpegged()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnToggleLowerUnpegged();
			PostAction();
		}

		[BeginAction("togglepegmidtexture")]
		public void TogglePegMidtexture()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnTogglePegMidtexture();
			PostAction();
		}

		[BeginAction("toggleslopeskew")]
		public void ToggleSlopeSkew()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnToggleSlopeSkew();
			PostAction();
		}

		[BeginAction("togglegravity")]
		public void ToggleGravity()
		{
			BuilderPlug.Me.UseGravity = !BuilderPlug.Me.UseGravity;

			string shortmessage = "Gravity is now " + (BuilderPlug.Me.UseGravity ? "ON" : "OFF") + ".";
			string message = shortmessage;
			string key = Actions.Action.GetShortcutKeyDesc(General.Actions.Current.ShortcutKey);

			if (!string.IsNullOrEmpty(key))
				message += $" Press '{key}' to toggle.";

			General.ToastManager.ShowToast("togglegravity", ToastType.INFO, "Changed gravity", message, new StatusInfo(StatusType.Action, shortmessage));
		}

		[BeginAction("resettexture")]
		public void ResetTexture()
		{
			PreAction(UndoGroup.None);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, false, false);
			foreach(IVisualEventReceiver i in objs) i.OnResetTextureOffset();
			PostAction();
		}

		[BeginAction("resettextureudmf")]
		public void ResetLocalOffsets() 
		{
			PreAction(UndoGroup.None);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, false, false);
			foreach(IVisualEventReceiver i in objs) i.OnResetLocalTextureOffset();
			PostAction();
		}

		[BeginAction("floodfilltextures")]
		public void FloodfillTextures()
		{
			PreAction(UndoGroup.None);
			GetTargetEventReceiver(false).OnTextureFloodfill();
			PostAction();
		}

		[BeginAction("texturecopyoffsets")]
		public void TextureCopyOffsets()
		{
			PreActionNoChange();
			GetTargetEventReceiver(true).OnCopyTextureOffsets(); //mxd
			PostAction();
		}

		[BeginAction("texturepasteoffsets")]
		public void TexturePasteOffsets()
		{
			PreAction(UndoGroup.None);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, false, false, false);
			foreach(IVisualEventReceiver i in objs) i.OnPasteTextureOffsets();
			PostAction();
		}

		[BeginAction("copyproperties")]
		public void CopyProperties()
		{
			PreActionNoChange();
			GetTargetEventReceiver(true).OnCopyProperties(); //mxd
			PostAction();
		}

		[BeginAction("pasteproperties")]
		public void PasteProperties()
		{
			PreAction(UndoGroup.None);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, false);
			foreach(IVisualEventReceiver i in objs) i.OnPasteProperties(false);
			PostAction();
		}

		//mxd
		[BeginAction("pastepropertieswithoptions")]
		public void PastePropertiesWithOptions()
		{
			// Which options to show?
			HashSet<int> added;
			var targettypes = new List<MapElementType>();
			var selection = new List<IVisualEventReceiver>();

			// Sectors selected?
			var obj = GetSelectedObjects(true, false, false, false, false);
			if(obj.Count > 0)
			{
				targettypes.Add(MapElementType.SECTOR);

				// Don't add duplicates
				added = new HashSet<int>();
				foreach(IVisualEventReceiver receiver in obj)
				{
					VisualGeometry vg = (VisualGeometry)receiver;
					if(vg != null && !added.Contains(vg.Sector.GetHashCode()))
					{
						selection.Add(receiver);
						added.Add(vg.Sector.GetHashCode());
					}
				}
			}

			// Sidedefs selected?
			obj = GetSelectedObjects(false, true, false, false, false);
			if(obj.Count > 0)
			{
				targettypes.Add(MapElementType.SIDEDEF);

				// Don't add duplicates
				added = new HashSet<int>();
				foreach(IVisualEventReceiver receiver in obj)
				{
					VisualGeometry vg = (VisualGeometry)receiver;
					if(vg != null && !added.Contains(vg.Sidedef.Line.GetHashCode()))
					{
						selection.Add(receiver);
						added.Add(vg.Sidedef.Line.GetHashCode());
					}
				}
			}

			// Things selected?
			obj = GetSelectedObjects(false, false, true, false, false);
			if(obj.Count > 0)
			{
				targettypes.Add(MapElementType.THING);

				// Don't add duplicates
				added = new HashSet<int>();
				foreach(IVisualEventReceiver receiver in obj)
				{
					VisualThing vt = (VisualThing)receiver;
					if(vt != null && !added.Contains(vt.Thing.GetHashCode()))
					{
						selection.Add(receiver);
						added.Add(vt.Thing.GetHashCode());
					}
				}
			}

			// Vertices selected?
			obj = GetSelectedObjects(false, false, false, true, false);
			if(obj.Count > 0)
			{
				targettypes.Add(MapElementType.VERTEX);

				// Don't add duplicates
				added = new HashSet<int>();
				foreach(IVisualEventReceiver receiver in obj)
				{
					VisualVertex vv = (VisualVertex)receiver;
					if(vv != null && !added.Contains(vv.Vertex.GetHashCode()))
					{
						selection.Add(receiver);
						added.Add(vv.Vertex.GetHashCode());
					}
				}
			}

			// Anything selected?
			if(selection.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "This action requires highlight or selection!");
				return;
			}

			// Show the form
			PastePropertiesOptionsForm form = new PastePropertiesOptionsForm();
			if(form.Setup(targettypes) && form.ShowDialog(General.Interface) == DialogResult.OK)
			{
				// Paste properties
				PreAction(UndoGroup.None);
				foreach(IVisualEventReceiver i in selection)i.OnPasteProperties(true);
				PostAction();
			}
		}

		//mxd. now we can insert things in Visual modes
		[BeginAction("insertitem", BaseAction = true)] 
		public void InsertThing()
		{
			Vector2D hitpos = GetHitPosition();

			if(!hitpos.IsFinite()) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Cannot insert thing here!");
				return;
			}
			
			ClearSelection();
			PreActionNoChange();
			General.Map.UndoRedo.ClearAllRedos();
			General.Map.UndoRedo.CreateUndo("Insert thing");

			Thing t = CreateThing(new Vector2D(hitpos.x, hitpos.y));

			if(t == null) 
			{
				General.Map.UndoRedo.WithdrawUndo();
				return;
			}

			// Edit the thing?
			if(BuilderPlug.Me.EditNewThing) General.Interface.ShowEditThings(new List<Thing> { t });

			//add thing to blockmap
			blockmap.AddThing(t);

			General.Interface.DisplayStatus(StatusType.Action, "Inserted a new thing.");
			PostAction();
		}

		//mxd
		[BeginAction("deleteitem", BaseAction = true)]
		public void Delete()
		{
			PreAction(UndoGroup.None);
			List<IVisualEventReceiver> objs = GetSelectedObjects(true, true, true, true, false);
			BeginTextureEditBatch();
			try
			{
				foreach (IVisualEventReceiver i in objs)
				{
					if (i is BaseVisualThing)
					{
						visiblethings.Remove((BaseVisualThing)i); // [ZZ] if any
						allthings.Remove(((BaseVisualThing)i).Thing);
					}
					i.OnDelete();
				}
			}
			finally
			{
				EndTextureEditBatch();
			}
			PostAction();

			ClearSelection();
		}

		//mxd
		[BeginAction("copyselection", BaseAction = true)]
		public void CopySelection() 
		{
			List<IVisualEventReceiver> objs = GetSelectedObjects(false, false, true, false, false);
			if(objs.Count == 0) return;

			copybuffer.Clear();
			foreach(IVisualEventReceiver i in objs) 
			{
				VisualThing vt = (VisualThing)i;
				if(vt != null) copybuffer.Add(new ThingCopyData(vt.Thing));
			}

			string rest = copybuffer.Count + (copybuffer.Count > 1 ? " things." : " thing.");
			General.Interface.DisplayStatus(StatusType.Info, "Copied " + rest);
		}

		//mxd
		[BeginAction("cutselection", BaseAction = true)]
		public void CutSelection() 
		{
			CopySelection();

			//Create undo
			string rest = copybuffer.Count + (copybuffer.Count > 1 ? " things." : " thing.");
			CreateUndo("Cut " + rest);
			General.Interface.DisplayStatus(StatusType.Info, "Cut " + rest);

			List<IVisualEventReceiver> objs = GetSelectedObjects(false, false, true, false, false);
			foreach(IVisualEventReceiver i in objs) 
			{
				BaseVisualThing thing = (BaseVisualThing)i;
                visiblethings.Remove(thing); // [ZZ] if any
                thing.Thing.Fields.BeforeFieldsChange();
				thing.Thing.Dispose();
				thing.Dispose();
			}

			General.Map.IsChanged = true;
			General.Map.ThingsFilter.Update();

            // [ZZ] Clear selected things.
            ClearSelection(false, false, true, false, false, false);

            // Update event lines
			if(General.Settings.GZShowEventLines)
				renderer.SetEventLines(General.Map.GetVisualEventLines(blockmap));
		}

		//mxd. We'll just use currently selected objects 
		[BeginAction("pasteselection", BaseAction = true)]
		public void PasteSelection() 
		{
			if(copybuffer.Count == 0)
			{
				TexturePaste(); // I guess we may paste a texture or two instead
				return;
			}
			
			Vector2D hitpos = GetHitPosition();

			if(!hitpos.IsFinite()) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Cannot paste here!");
				return;
			}

			hitpos.x = Math.Round(hitpos.x);
			hitpos.y = Math.Round(hitpos.y);

			string rest = copybuffer.Count + (copybuffer.Count > 1 ? " things." : " thing.");
			General.Map.UndoRedo.CreateUndo("Paste " + rest);
			General.Interface.DisplayStatus(StatusType.Info, "Pasted " + rest);
			
			PreActionNoChange();
			ClearSelection();

			//get translated positions
			Vector3D[] coords = new Vector3D[copybuffer.Count];
			for(int i = 0; i < copybuffer.Count; i++ )
				coords[i] = copybuffer[i].Position;

			Vector3D[] translatedCoords = TranslateCoordinates(coords, hitpos, true);

			//create things from copyBuffer
			for(int i = 0; i < copybuffer.Count; i++) 
			{
				Thing t = CreateThing(new Vector2D());
				if(t != null) 
				{
					copybuffer[i].ApplyTo(t);
					t.Move(translatedCoords[i]);
					//add thing to blockmap
					blockmap.AddThing(t);
				}
			}

			General.Map.IsChanged = true;
			General.Map.ThingsFilter.Update();

			PostAction();
		}

		//mxd. Rotate clockwise
		[BeginAction("rotateclockwise")]
		public void RotateCW() 
		{
			RotateThingsAndTextures(General.Map.Config.DoomThingRotationAngles ? 45 : 5, 5);
		}

		//mxd. Rotate counterclockwise
		[BeginAction("rotatecounterclockwise")]
		public void RotateCCW() 
		{
			RotateThingsAndTextures(General.Map.Config.DoomThingRotationAngles ? -45 : -5, - 5);
		}

		//mxd
		private void RotateThingsAndTextures(int thingangleincrement, int textureangleincrement) 
		{
			PreAction(UndoGroup.ThingAngleChange);

			List<IVisualEventReceiver> selection = GetSelectedObjects(true, false, true, false, false);
			if(selection.Count == 0) return;

			foreach(IVisualEventReceiver obj in selection) 
			{
				if(obj is BaseVisualThing) 
				{
					BaseVisualThing t = (BaseVisualThing)obj;

					int newangle = t.Thing.AngleDoom + thingangleincrement;
					if(General.Map.Config.DoomThingRotationAngles) newangle = newangle / 45 * 45;
					t.SetAngle(General.ClampAngle(newangle));

					// Visual sectors may be affected by this thing...
					if(thingdata.ContainsKey(t.Thing))
					{
						// Update what must be updated
						ThingData td = GetThingData(t.Thing);
						foreach(KeyValuePair<Sector, bool> s in td.UpdateAlso)
						{
							if(VisualSectorExists(s.Key))
							{
								BaseVisualSector vs = GetBaseVisualSector(s.Key);
								vs.UpdateSectorGeometry(s.Value);
							}
						}
					}
				}
				else if(obj is VisualFloor) 
				{
					VisualFloor vf = (VisualFloor)obj;
					vf.OnChangeTextureRotation(General.ClampAngle(vf.GetControlSector().Fields.GetValue("rotationfloor", 0.0) + textureangleincrement));
				} 
				else if(obj is VisualCeiling) 
				{
					VisualCeiling vc = (VisualCeiling)obj;
					vc.OnChangeTextureRotation(General.ClampAngle(vc.GetControlSector().Fields.GetValue("rotationceiling", 0.0) + textureangleincrement));
				}
			}

			PostAction();
		}

		//mxd. Change pitch clockwise
		[BeginAction("pitchclockwise")]
		public void PitchCW() 
		{
			ChangeThingsPitch(-5);
		}

		//mxd. Change pitch counterclockwise
		[BeginAction("pitchcounterclockwise")]
		public void PitchCCW() 
		{
			ChangeThingsPitch(5);
		}

		//mxd
		private void ChangeThingsPitch(int increment) 
		{
			PreAction(UndoGroup.ThingPitchChange);

			List<IVisualEventReceiver> selection = GetSelectedObjects(false, false, true, false, false);
			if(selection.Count == 0) return;

			foreach(IVisualEventReceiver obj in selection) 
			{
				BaseVisualThing t = (BaseVisualThing)obj;
				if(t != null) t.SetPitch(General.ClampAngle(t.Thing.Pitch + increment));
			}

			PostAction();
		}

		//mxd. Change pitch clockwise
		[BeginAction("rollclockwise")]
		public void RollCW() 
		{
			ChangeThingsRoll(-5);
		}

		//mxd. Change pitch counterclockwise
		[BeginAction("rollcounterclockwise")]
		public void RollCCW() 
		{
			ChangeThingsRoll(5);
		}

		//mxd
		private void ChangeThingsRoll(int increment) 
		{
			PreAction(UndoGroup.ThingRollChange);

			List<IVisualEventReceiver> selection = GetSelectedObjects(false, false, true, false, false);
			if(selection.Count == 0) return;

			foreach(IVisualEventReceiver obj in selection) 
			{
				BaseVisualThing t = (BaseVisualThing)obj;
				if(t != null) t.SetRoll(General.ClampAngle(t.Thing.Roll + increment));
			}

			PostAction();
		}

		//mxd
		[BeginAction("gztoggleenhancedrendering", BaseAction = true)]
		public void ToggleEnhancedRendering() 
		{
			foreach(VisualSector vs in allsectors.Values)
			{
				BaseVisualSector sector = vs as BaseVisualSector;
				if(sector != null) sector.Changed = true;
			}

			// Actual toggling is done in MainForm.ToggleEnhancedRendering(), so we only need to update the view here
			RebuildElementData();
			UpdateChangedObjects();
		}

		//sphere
		[BeginAction("togglesrb2invisiblefof", BaseAction = true)]
		public void ToggleSRB2InvisibleFOFRendering()
		{
			// Actual toggling is done in MainForm.ToggleSRB2InvisibleFOFRendering(), so we only need to update the view here
			RefreshInvisibleFOFPreview();
		}

		//mxd
		[BeginAction("thingaligntowall")]
		public void AlignThingsToWall() 
		{
			List<VisualThing> visualThings = GetSelectedVisualThings(true);

			if(visualThings.Count == 0) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "This action requires selected Things!");
				return;
			}

			List<Thing> things = new List<Thing>();

			foreach(VisualThing vt in visualThings)
				things.Add(vt.Thing);

			// Make undo
			if(things.Count > 1) 
			{
				General.Map.UndoRedo.CreateUndo("Align " + things.Count + " things");
				General.Interface.DisplayStatus(StatusType.Action, "Aligned " + things.Count + " things.");
			} 
			else 
			{
				General.Map.UndoRedo.CreateUndo("Align thing");
				General.Interface.DisplayStatus(StatusType.Action, "Aligned a thing.");
			}

			//align things
			foreach(Thing t in things) 
			{
				HashSet<Linedef> excludedLines = new HashSet<Linedef>();
				bool aligned;

				do 
				{
					Linedef l = General.Map.Map.NearestLinedef(t.Position, excludedLines);
					aligned = Tools.TryAlignThingToLine(t, l);

					if(!aligned) 
					{
						excludedLines.Add(l);

						if(excludedLines.Count == General.Map.Map.Linedefs.Count) 
						{
							ThingTypeInfo tti = General.Map.Data.GetThingInfo(t.Type);
							General.ErrorLogger.Add(ErrorType.Warning, "Unable to align " + tti.Title + " (index " + t.Index + ") to any linedef!");
							aligned = true;
						}
					}
				} while(!aligned);
			}

			//apply changes to Visual Things
			for(int i = 0; i < visualThings.Count; i++) 
			{
				BaseVisualThing t = (BaseVisualThing)visualThings[i];
				t.Changed = true;

				// Update what must be updated
				ThingData td = GetThingData(t.Thing);
				foreach(KeyValuePair<Sector, bool> s in td.UpdateAlso) 
				{
					if(VisualSectorExists(s.Key)) 
					{
						BaseVisualSector vs = GetBaseVisualSector(s.Key);
						vs.UpdateSectorGeometry(s.Value);
					}
				}
			}

			UpdateChangedObjects();
			ShowTargetInfo();
		}

		//mxd
		[BeginAction("lookthroughthing")]
		public void LookThroughThing() 
		{
			List<VisualThing> visualThings = GetSelectedVisualThings(true);

			if(visualThings.Count != 1) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Look Through Selection action requires 1 selected Thing!");
				return;
			}

			//set position and angles
			Thing t = visualThings[0].Thing;
			if((t.Type == 9072 || t.Type == 9073) && t.Args[3] > 0) //AimingCamera or MovingCamera with target?
			{ 
				//position
				if(t.Type == 9072 && (t.Args[0] > 0 || t.Args[1] > 0)) //positon MovingCamera at targeted interpolation point
				{ 
					int ipTag = t.Args[0] + (t.Args[1] << 8);
					Thing ip = null;

					//find interpolation point
					foreach(Thing tgt in General.Map.Map.Things) 
					{
						if(tgt.Tag == ipTag && tgt.Type == 9070) 
						{
							ip = tgt;
							break;
						}
					}

					if(ip != null) 
					{
						VisualThing vTarget = !VisualThingExists(ip) ? CreateVisualThing(ip) : GetVisualThing(ip);
						Vector3D targetPos;
						if(vTarget == null) 
						{
							targetPos = ip.Position;
							if(ip.Sector != null) targetPos.z += ip.Sector.FloorHeight;
						} 
						else 
						{
							targetPos = vTarget.CenterV3D;
						}

						General.Map.VisualCamera.Position = targetPos; //position at interpolation point
					} 
					else 
					{
						General.Map.VisualCamera.Position = visualThings[0].CenterV3D; //position at camera
					}

				}
				else
				{
					General.Map.VisualCamera.Position = visualThings[0].CenterV3D; //position at camera
				}

				//angle
				Thing target = null;

				foreach(Thing tgt in General.Map.Map.Things) 
				{
					if(tgt.Tag == t.Args[3]) 
					{
						target = tgt;
						break;
					}
				}

				if(target == null) 
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Camera target with Tag " + t.Args[3] + " does not exist!");
					General.Map.VisualCamera.AngleXY = t.Angle - Angle2D.PI;
					General.Map.VisualCamera.AngleZ = Angle2D.PI;
				} 
				else 
				{
					VisualThing vTarget = !VisualThingExists(target) ? CreateVisualThing(target) : GetVisualThing(target);
					Vector3D targetPos;
					if(vTarget == null) 
					{
						targetPos = target.Position;
						if(target.Sector != null) targetPos.z += target.Sector.FloorHeight;
					} 
					else 
					{
						targetPos = vTarget.CenterV3D;
					}

					bool pitch = (t.Args[2] & 4) != 0;
					Vector3D delta = General.Map.VisualCamera.Position - targetPos;
					General.Map.VisualCamera.AngleXY = delta.GetAngleXY();
					General.Map.VisualCamera.AngleZ = pitch ? -delta.GetAngleZ() : Angle2D.PI;
				}
			} 
			else if((t.Type == 9025 || t.Type == 9073 || t.Type == 9070) && t.Args[0] != 0) //InterpolationPoint, SecurityCamera or AimingCamera with pitch?
			{ 
				General.Map.VisualCamera.Position = visualThings[0].CenterV3D; //position at camera
				General.Map.VisualCamera.AngleXY = t.Angle - Angle2D.PI;
				General.Map.VisualCamera.AngleZ = Angle2D.PI + Angle2D.DegToRad(t.Args[0]);
			} 
			else //nope, just a generic thing
			{ 
				General.Map.VisualCamera.Position = visualThings[0].CenterV3D; //position at thing
				General.Map.VisualCamera.AngleXY = t.Angle - Angle2D.PI;

				if (General.Map.UDMF)
					General.Map.VisualCamera.AngleZ = Angle2D.DegToRad(t.Pitch) + Angle2D.PI;
				else
					General.Map.VisualCamera.AngleZ = Angle2D.PI;
			}
		}

		//mxd
		[BeginAction("toggleslope")]
		public void ToggleSlope() 
		{
			List<VisualGeometry> selection = GetSelectedSurfaces();

			if(selection.Count == 0) 
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Toggle Slope action requires selected surfaces!");
				return;
			}

			List<BaseVisualSector> toUpdate = new List<BaseVisualSector>();
			General.Map.UndoRedo.CreateUndo("Toggle Slope");

			//check selection
			foreach(VisualGeometry vg in selection) 
			{
				bool update = false;

				//assign/remove action
				if(vg.GeometryType == VisualGeometryType.WALL_LOWER) 
				{
					if(vg.Sidedef.Line.Action == 0 || (vg.Sidedef.Line.Action == 700 && vg.Sidedef.Line.Args[0] == 0)) 
					{
						//check if the sector already has floor slopes
						foreach(Sidedef side in vg.Sidedef.Sector.Sidedefs) 
						{
							if(side == vg.Sidedef || side.Line.Action != 700) continue;

							int arg = (side == side.Line.Front ? 1 : 2);

							if(side.Line.Args[0] == arg) 
							{
								//if only floor is affected, remove action
								if(side.Line.Args[1] == 0)
									side.Line.Action = 0;
								else //clear floor alignment
									side.Line.Args[0] = 0;
							}
						}

						//set action
						vg.Sidedef.Line.Action = 700;
						vg.Sidedef.Line.Args[0] = (vg.Sidedef == vg.Sidedef.Line.Front ? 1 : 2);
						update = true;
					}
				} 
				else if(vg.GeometryType == VisualGeometryType.WALL_UPPER) 
				{
					if(vg.Sidedef.Line.Action == 0 || (vg.Sidedef.Line.Action == 700 && vg.Sidedef.Line.Args[1] == 0)) 
					{
						//check if the sector already has ceiling slopes
						foreach(Sidedef side in vg.Sidedef.Sector.Sidedefs) 
						{
							if(side == vg.Sidedef || side.Line.Action != 700) continue;

							int arg = (side == side.Line.Front ? 1 : 2);

							if(side.Line.Args[1] == arg) 
							{
								//if only ceiling is affected, remove action
								if(side.Line.Args[0] == 0)
									side.Line.Action = 0;
								else //clear ceiling alignment
									side.Line.Args[1] = 0;
							}
						}

						//set action
						vg.Sidedef.Line.Action = 700;
						vg.Sidedef.Line.Args[1] = (vg.Sidedef == vg.Sidedef.Line.Front ? 1 : 2);
						update = true;
					}
				} 
				else if(vg.GeometryType == VisualGeometryType.CEILING) 
				{
					//check if the sector has ceiling slopes
					foreach(Sidedef side in vg.Sector.Sector.Sidedefs) 
					{
						if(side.Line.Action != 700)	continue;

						int arg = (side == side.Line.Front ? 1 : 2);

						if(side.Line.Args[1] == arg) 
						{
							//if only ceiling is affected, remove action
							if(side.Line.Args[0] == 0)
								side.Line.Action = 0;
							else //clear ceiling alignment
								side.Line.Args[1] = 0;

							update = true;
						}
					}
				} 
				else if(vg.GeometryType == VisualGeometryType.FLOOR) 
				{
					//check if the sector has floor slopes
					foreach(Sidedef side in vg.Sector.Sector.Sidedefs) 
					{
						if(side.Line.Action != 700)	continue;

						int arg = (side == side.Line.Front ? 1 : 2);

						if(side.Line.Args[0] == arg) 
						{
							//if only floor is affected, remove action
							if(side.Line.Args[1] == 0)
								side.Line.Action = 0;
							else //clear floor alignment
								side.Line.Args[0] = 0;

							update = true;
						}
					}
				}

				//add to update list
				if(update) toUpdate.Add((BaseVisualSector)vg.Sector);
			}

			//update changed geometry
			if(toUpdate.Count > 0) 
			{
				RebuildElementData();

				foreach(BaseVisualSector vs in toUpdate)
					vs.UpdateSectorGeometry(true);

				UpdateChangedObjects();
				ClearSelection();
				ShowTargetInfo();
			}

			General.Interface.DisplayStatus(StatusType.Action, "Toggled Slope for " + toUpdate.Count + (toUpdate.Count == 1 ? " surface." : " surfaces."));
		}

		//mxd
		[BeginAction("alphabasedtexturehighlighting")]
		public void ToggleAlphaBasedTextureHighlighting()
		{
			BuilderPlug.Me.AlphaBasedTextureHighlighting = !BuilderPlug.Me.AlphaBasedTextureHighlighting;
			General.Interface.DisplayStatus(StatusType.Info, "Alpha-based textures highlighting is " + (BuilderPlug.Me.AlphaBasedTextureHighlighting ? "ENABLED" : "DISABLED"));
		}

		// biwa
		[BeginAction("visualpaintselect")]
		protected virtual void OnPaintSelectBegin()
		{
			paintselectpressed = true;
			GetTargetEventReceiver(true).OnPaintSelectBegin();
		}

		// biwa
		[EndAction("visualpaintselect")]
		protected virtual void OnPaintSelectEnd()
		{
			paintselectpressed = false;
			paintselecttype = null;
			lastpaintselectedobject1 = null;
			lastpaintselectedobject2 = null;
			GetTargetEventReceiver(true).OnPaintSelectEnd();
		}

		[BeginAction("togglevisualslopepicking")]
		public void ToggleVisualSidedefSlopePicking()
		{
			if (!General.Map.UDMF)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is supported in UDMF only.");
				return;
			}
			else if(!General.Map.Config.PlaneEquationSupport)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is not supported in this game configuration.");
				return;
			}

			if (pickingmode != PickingMode.SidedefSlopeHandles)
			{
				EnsureSlopeHandles();
				pickingmode = PickingMode.SidedefSlopeHandles;
			}
			else
			{
				pickingmode = PickingMode.Default;

				// Clear smart pivot handles, otherwise it will keep being displayed
				foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in allslopehandles)
					foreach (VisualSlope checkhandle in kvp.Value)
						if (checkhandle.SmartPivot && !(checkhandle.Selected || checkhandle.Pivot))
						{
							checkhandle.SmartPivot = false;
							usedslopehandles.Remove(checkhandle);
						}
			}
		}

		[BeginAction("togglevisualvertexslopepicking")]
		public void ToggleVisualVertexSlopePicking()
		{
			if (!General.Map.UDMF)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is supported in UDMF only.");
				return;
			}
			else if (!General.Map.Config.PlaneEquationSupport)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is not supported in this game configuration.");
				return;
			}

			if (pickingmode != PickingMode.VertexSlopeHandles)
			{
				EnsureSlopeHandles();
				pickingmode = PickingMode.VertexSlopeHandles;
			}
			else
			{
				pickingmode = PickingMode.Default;

				// Clear smart pivot handles, otherwise it will keep being displayed
				foreach (KeyValuePair<Sector, List<VisualSlope>> kvp in allslopehandles)
					foreach (VisualSlope checkhandle in kvp.Value)
						if (checkhandle.SmartPivot && !(checkhandle.Selected || checkhandle.Pivot))
						{
							checkhandle.SmartPivot = false;
							usedslopehandles.Remove(checkhandle);
						}
			}
		}

		[BeginAction("togglevisualvertexslopeadjacentselection")]
		public void ToggleVisualVertexSlopeAdjacentSelection()
		{
			if (!General.Map.UDMF)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is not supported in this game configuration.");
				return;
			}
			else if (!General.Map.Config.PlaneEquationSupport)
			{
				General.ToastManager.ShowToast(ToastMessages.VISUALSLOPING, ToastType.WARNING, "Visual sloping", "Visual sloping is not supported in this game configuration.");
				return;
			}

			BuilderPlug.Me.SelectAdjacentVisualVertexSlopeHandles = !BuilderPlug.Me.SelectAdjacentVisualVertexSlopeHandles;

			General.Interface.DisplayStatus(StatusType.Action, "Adjacant selection of visual vertex slop handles is " + (BuilderPlug.Me.SelectAdjacentVisualVertexSlopeHandles ? "ENABLED" : "DISABLED"));
		}


		[BeginAction("resetslope")]
		public void ResetSlope()
		{
			List<IVisualEventReceiver> selectedsectors = GetSelectedObjects(true, false, false, false, false);
			if (selectedsectors.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You need to select at least one floor or ceiling to reset slope.");
				return;
			}

			General.Map.UndoRedo.CreateUndo("Reset plane slope");

			int numfloors = 0;
			int numceilings = 0;

			// Reset slope
			foreach (BaseVisualGeometrySector bvgs in selectedsectors)
			{
				SectorLevel level = bvgs.Level;
				bool applytoceiling = false;
				if (level.extrafloor != null)
				{
					// The top side of 3D floors is the ceiling of the sector, but it's a "floor" in UDB, so the
					// ceiling of the control sector has to be modified
					if (level.type == SectorLevelType.Floor)
						applytoceiling = true;
				}
				else
				{
					if (level.type == SectorLevelType.Ceiling)
						applytoceiling = true;
				}

				if (applytoceiling)
				{
					// Set the ceiling height to something hopefully sensible
					// biwa. Do not reset to the z position of the plane of the center of the sector anymore, since 
					// that will result in pretty crazy values of 3D floor control sectors
					//level.sector.CeilHeight = (int)Math.Round(level.plane.GetZ(level.sector.BBox.X + level.sector.BBox.Width / 2, level.sector.BBox.Y + level.sector.BBox.Height / 2));

					level.sector.CeilSlopeOffset = double.NaN;
					level.sector.CeilSlope = new Vector3D();
					numceilings++;
				}
				else
				{
					// Set the floor height to something hopefully sensible
					// biwa. Do not reset to the z position of the plane of the center of the sector anymore, since 
					// that will result in pretty crazy values of 3D floor control sectors
					//level.sector.FloorHeight = (int)Math.Round(level.plane.GetZ(level.sector.BBox.X + level.sector.BBox.Width / 2, level.sector.BBox.Y + level.sector.BBox.Height / 2));

					level.sector.FloorSlopeOffset = double.NaN;
					level.sector.FloorSlope = new Vector3D();
					numfloors++;
				}

				// Rebuild sector
				BaseVisualSector vs;
				if (VisualSectorExists(level.sector))
				{
					vs = GetBaseVisualSector(level.sector);
				}
				else
				{
					vs = CreateBaseVisualSector(level.sector);
				}

				if (vs != null) vs.UpdateSectorGeometry(true);
			}

			string ptype = "plane";
			if (numfloors == 0) ptype = "ceiling";
			else if (numceilings == 0) ptype = "floor";

			UpdateChangedObjects();

			General.Interface.DisplayStatus(StatusType.Action, string.Format("{1} {0} slopes reset.", ptype, numfloors+numceilings));
		}

		[BeginAction("slopebetweenhandles")]
		public void SlopeBetweenHandles()
		{
			List<IVisualEventReceiver> selectedsectors = GetSelectedObjects(true, false, false, false, false);
			if (selectedsectors.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You need to select floors or ceilings to slope between slope handles.");
				return;
			}

			List<VisualSidedefSlope> handles = GetSlopeHandlePair();

			if (handles.Count != 2)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You need to select exactly two slope handles.");
				return;
			}

			General.Map.UndoRedo.CreateUndo("Slope between slope handles");

			// Create the new plane
			Vector3D p1 = new Vector3D(handles[0].Sidedef.Line.Start.Position, handles[0].Level.plane.GetZ(handles[0].Sidedef.Line.Start.Position));
			Vector3D p2 = new Vector3D(handles[0].Sidedef.Line.End.Position, handles[0].Level.plane.GetZ(handles[0].Sidedef.Line.End.Position));
			Vector3D p3 = new Vector3D(handles[1].Sidedef.Line.Line.GetCoordinatesAt(0.5f), handles[1].Level.plane.GetZ(handles[1].Sidedef.Line.Line.GetCoordinatesAt(0.5f)));
			Plane plane = new Plane(p1, p2, p3, true);

			// Apply slope
			foreach (BaseVisualGeometrySector bvgs in selectedsectors)
			{
				VisualSidedefSlope.ApplySlope(bvgs.Level, plane, this);
				bvgs.Sector.UpdateSectorGeometry(true);
			}

			UpdateChangedObjects();

			General.Interface.DisplayStatus(StatusType.Action, "Sloped between slope handles.");
		}

		/// <summary>
		/// Applies plane equation slopes to selected sectors, based on the selected slope handles
		/// </summary>
		[BeginAction("archbetweenhandles")]
		public void ArchBetweenHandles()
		{
			List<IVisualEventReceiver> selectedsectors = GetSelectedObjects(true, false, false, false, false);

			if (selectedsectors.Count < 2)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You need to select at least two floors and ceilings to slope arch between slope handles.");
				return;
			}

			List<VisualSidedefSlope> handles = GetSlopeHandlePair();

			if (handles.Count != 2)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You need to select exactly two slope handles.");
				return;
			}

			General.Map.UndoRedo.CreateUndo("Arch between slope handles");

			Vector3D p1 = handles[0].GetCenterPoint();
			Vector3D p2 = handles[1].GetCenterPoint();
			double linelength = Line2D.GetLength(p2.x - p1.x, p2.y - p1.y);
			double zdiff = Math.Abs(p1.z - p2.z);
			double theta;
			double offsetangle;

			// Compute theta and the offset angle. Special handling if the slope handles are at the same height
			if (zdiff == 0.0)
			{
				theta = Math.PI;
				offsetangle = 0.0;
			}
			else
			{
				theta = Math.Atan(zdiff / linelength) * 2;
				offsetangle = Math.PI / 2.0;

				if (p2.z < p1.z)
					offsetangle -= theta;
			}

			SlopeArcher sa = new SlopeArcher(this, selectedsectors, handles[0], handles[1], theta, offsetangle, 1.0);

			SlopeArchForm saf = new SlopeArchForm(sa);
			saf.UpdateChangedObjects += Interface_OnUpdateChangedObjects;
			DialogResult result = saf.ShowDialog();
			saf.UpdateChangedObjects -= Interface_OnUpdateChangedObjects;

			if (result == DialogResult.Cancel)
				General.Map.UndoRedo.WithdrawUndo();
			else
			{
				UpdateChangedObjects();

				General.Interface.DisplayStatus(StatusType.Action, "Arched between slope handles.");
			}
		}

		/// <summary>
		/// Applies the Visual Mode's current camera pitch and yaw to the selected things
		/// </summary>
		[BeginAction("applycamerarotationtothings")]
		public void ApplyCameraRotationToThings()
		{
			List<Thing> things = GetSelectedThings();

			if(things.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Can't apply camera rotation to things: no things selected.");
				return;
			}

			General.Map.UndoRedo.CreateUndo("Apply camera rotation to things");

			foreach (Thing t in things)
			{
				t.Rotate(General.Map.VisualCamera.AngleXY - Angle2D.PI);

				if (General.Map.UDMF)
					t.SetPitch((int)Angle2D.RadToDeg(General.Map.VisualCamera.AngleZ - Angle2D.PI));

				((BaseVisualThing)allthings[t]).Rebuild();

				General.Interface.DisplayStatus(StatusType.Action, $"Applied camera rotation and pitch to {things.Count} thing{(things.Count == 1 ? "" : "s")}.");
			}
		}

		[BeginAction("selectoppositeside")]
		public void SelectOppositeSide()
		{
			foreach (var o in selectedobjects.ToList())
			{
				IVisualEventReceiver otherObject = null;

				if (o is VisualFloor floor && floor.ExtraFloor == null)
					otherObject = floor.Sector.Ceiling;
				else if (o is VisualCeiling ceiling && ceiling.ExtraFloor == null)
					otherObject = ceiling.Sector.Floor;
				else if (o is VisualFloor extraFloor)
					otherObject = extraFloor.Sector.ExtraCeilings.Find(ec => ec.ExtraFloor == extraFloor.ExtraFloor);
				else if (o is VisualCeiling extraCeiling)
					otherObject = extraCeiling.Sector.ExtraFloors.Find(ef => ef.ExtraFloor == extraCeiling.ExtraFloor);
				else if (o is VisualMiddleDouble side)
				{
					BaseVisualSector otherSector = GetBaseVisualSector(side.Sidedef.Other.Sector);
					otherSector.PrepareSidedefGeometry(side.Sidedef.Other);
					otherObject = otherSector.GetSidedefParts(side.Sidedef.Other).middledouble;
				}
				else if (o is VisualSidedefSlope sideHandle && sideHandle.Sidedef.Other != null)
				{
					Sidedef otherSide = sideHandle.Sidedef.Other;
					BaseVisualSector visSector = GetBaseVisualSector(sideHandle.Sidedef.Sector);
					BaseVisualSector otherVisSector = GetBaseVisualSector(otherSide.Sector);

					otherObject = SidedefSlopeHandles[otherSide.Sector].Find(otherHandle => {
						if ((otherHandle as VisualSidedefSlope).Sidedef != otherSide)
							return false;

						var v1 = sideHandle.Sidedef.Line.Start.Position;
						var v2 = sideHandle.Sidedef.Line.End.Position;

						var p1 = sideHandle.Level.plane;
						var p2 = (otherHandle as VisualSidedefSlope).Level.plane;

						return Math.Abs(p1.GetZ(v1) - p2.GetZ(v1)) < 1.0 && Math.Abs(p1.GetZ(v2) - p2.GetZ(v2)) < 1.0;
					}) as VisualSidedefSlope;
				}

				if (otherObject != null && !otherObject.Selected)
				{
					otherObject.OnSelectBegin();
					otherObject.OnSelectEnd();
				}
			}
		}

		[BeginAction("selectgroup")]
		public void SelectGroup()
		{
			PickTargetUnlocked();

			if (HighlightedTarget == null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You must highlight an element to select a group.");
				return;
			}

			var selectablegroups = FindGroups3DWithObject(HighlightedTarget as IVisualEventReceiver);
			if (!selectablegroups.Any())
			{
				General.Interface.DisplayStatus(StatusType.Warning, "The highlighted element does not belong to any group.");
				return;
			}

			var selectedgroupindex = selectablegroups.IndexOf(SelectedGroup3D);
			var nextgroupindex = selectedgroupindex + 1;

			if (selectedgroupindex >= 0)
			{
				foreach (var e in selectablegroups[selectedgroupindex])
				{
					var o = GetObjectByMapElement3D(e);
					if (o != null && o.Selected)
					{
						o.OnSelectBegin();
						o.OnSelectEnd();
					}
				}
			}

			if (nextgroupindex < selectablegroups.Count)
			{
				SelectedGroup3D = selectablegroups[nextgroupindex];
				LastSelectedGroup3D = SelectedGroup3D;

				foreach (var e in SelectedGroup3D)
				{
					var o = GetObjectByMapElement3D(e);
					if (o != null && !o.Selected)
					{
						o.OnSelectBegin();
						o.OnSelectEnd();
					}
				}

				General.Interface.DisplayStatus(StatusType.Info, "Selected group " + (nextgroupindex + 1) + "/" + selectablegroups.Count + ".");
			}
			else
			{
				SelectedGroup3D = null;
				LastSelectedGroup3D = null;
				General.Interface.DisplayStatus(StatusType.Info, "Deselected group.");
			}
		}

		[BeginAction("creategroup")]
		public void CreateGroup()
		{
			General.Map.Map.CleanupGroups3D();

			if (FindSelectedGroup3D() != null) return;

			var group = new Group3D();

			foreach (var o in selectedobjects)
				if (o.AsMapElement3D != null)
					group.Add(o.AsMapElement3D);

			foreach (var e in group)
				General.Map.Map.GroupsByMapElement3D.Add(e, group);

			if (!group.Any())
			{
				General.Interface.DisplayStatus(StatusType.Warning, "You must select at least one element to create a group.");
				return;
			}

			General.Map.Map.Groups3D.Add(group);
			SelectedGroup3D = group;
			LastSelectedGroup3D = group;

			General.Interface.DisplayStatus(StatusType.Action, "Created a new group with " + group.Count + " elements.");
		}

		[BeginAction("updateordeletelastselectedgroup")]
		public void UpdateOrDeleteLastSelectedGroup()
		{
			General.Map.Map.CleanupGroups3D();

			if (LastSelectedGroup3D == null || !General.Map.Map.Groups3D.Contains(LastSelectedGroup3D))
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Select a group first.");
				return;
			}

			foreach (var e in LastSelectedGroup3D)
				if (!e.IsDisposed)
					General.Map.Map.GroupsByMapElement3D.Remove(e, LastSelectedGroup3D);
			LastSelectedGroup3D.Clear();

			foreach (var o in selectedobjects)
			{
				var e = o.AsMapElement3D;
				if (e != null)
				{
					LastSelectedGroup3D.Add(e);
					General.Map.Map.GroupsByMapElement3D.Add(e, LastSelectedGroup3D);
				}
			}

			if (LastSelectedGroup3D.Any())
			{
				General.Interface.DisplayStatus(StatusType.Action, "Updated a group.");
			}
			else
			{
				General.Map.Map.Groups3D.Remove(LastSelectedGroup3D);
				LastSelectedGroup3D = null;

				General.Interface.DisplayStatus(StatusType.Action, "Deleted a group.");
			}
		}

		#endregion

		#region ================== Texture Alignment

		//mxd. If checkSelectedSidedefParts is set to true, only selected linedef parts will be aligned (when a sidedef has both top and bottom parts, but only bottom is selected, top texture won't be aligned)
		internal HashSet<Sidedef> AutoAlignTextures(BaseVisualGeometrySidedef start, ImageData texture, bool alignx, bool aligny, bool resetsidemarks, bool checkSelectedSidedefParts)
		{
			HashSet<Sidedef> changes = new HashSet<Sidedef>();
			if(General.Map.UDMF && General.Map.Config.UseLocalSidedefTextureOffsets)
				AutoAlignTexturesUDMF(start, texture, alignx, aligny, resetsidemarks, checkSelectedSidedefParts, changes);
			else
				AutoAlignTextures(start, texture, alignx, aligny, resetsidemarks, changes);

			return changes;
		}

		// Several visual surfaces may be edited by one command. Updating the used
		// texture list scans the entire map, so defer it until the command completes
		// instead of repeating the same scan once for every selected surface.
		internal void RequestUsedTexturesUpdate()
		{
			if(textureeditbatchdepth > 0)
				usedtexturesupdatepending = true;
			else
				General.Map.Data.UpdateUsedTextures();
		}

		private void BeginTextureEditBatch()
		{
			textureeditbatchdepth++;
		}

		private void EndTextureEditBatch()
		{
			if(textureeditbatchdepth == 0) return;

			textureeditbatchdepth--;
			if(textureeditbatchdepth == 0 && usedtexturesupdatepending)
			{
				usedtexturesupdatepending = false;
				General.Map.Data.UpdateUsedTextures();
			}
		}

		internal void UpdateAlignedSidedefs(IEnumerable<Sidedef> changes, bool prioritizeVisible = false)
		{
			int queued = 0;
			int refreshed = 0;
			HashSet<VisualSector> refreshedsectors = new HashSet<VisualSector>();
			HashSet<Sidedef> candidates = new HashSet<Sidedef>();
			foreach(Sidedef sd in changes)
			{
				if(sd != null && !sd.IsDisposed)
					candidates.Add(sd);
			}

			if(prioritizeVisible && candidates.Count > 0)
			{
				HashSet<Linedef> visiblelineset = new HashSet<Linedef>(visiblelines);
				foreach(Sidedef sd in new List<Sidedef>(candidates))
				{
					if(!visiblelineset.Contains(sd.Line) || !VisualSectorExists(sd.Sector)) continue;
					BaseVisualSector vs = GetBaseVisualSector(sd.Sector);
					if(!visiblesectors.Contains(vs)) continue;
					vs.RefreshSidedefGeometry(sd);
					refreshedsectors.Add(vs);
					candidates.Remove(sd);
					refreshed++;
				}
				RefreshVisibleSectorGeometry(refreshedsectors);
			}

			foreach(Sidedef sd in candidates) queued += QueueAlignedSidedef(sd);

			if(queued > 0 && !alignmentrefreshactive)
			{
				alignmentrefreshactive = true;
				alignmentrefreshstart = Clock.CurrentTime;
				General.WriteLogLine("Queued " + pendingalignedsidedefs.Count
					+ " Visual Mode sidedef refreshes after texture alignment");
			}

			// Ordinary edits are small enough to complete before the next frame. Large
			// chains retain the bounded queue, but every wall already on screen was
			// refreshed above so the action still appears instantaneous.
			if(pendingalignedsidedefs.Count <= 96)
				while(pendingalignedsidedefs.Count > 0) ProcessPendingAlignedSidedefs();

			if(refreshed > 0)
			{
				InvalidatePickTarget(true);
				General.Interface.RedrawDisplay();
			}
		}

		// Property dialogs suspend the regular processing timer while they are open, so
		// the normal frame-budgeted sidedef queue cannot provide a live preview. Refresh
		// only wall geometry that is already on screen and draw one frame immediately;
		// the complete (including off-screen) refresh is still queued when the dialog
		// closes. This keeps scale/offset previews responsive even for huge selections.
		internal void UpdateAlignedSidedefsRealtime(IEnumerable<Sidedef> changes)
		{
			HashSet<Sidedef> candidates = new HashSet<Sidedef>();
			foreach(Sidedef sd in changes)
			{
				if(sd != null && !sd.IsDisposed) candidates.Add(sd);
			}
			if(candidates.Count == 0) return;

			HashSet<Sidedef> refreshed = new HashSet<Sidedef>();
			HashSet<VisualSector> refreshedsectors = new HashSet<VisualSector>();
			HashSet<Linedef> visiblelineset = new HashSet<Linedef>(visiblelines);
			foreach(Sidedef sd in candidates)
			{
				if(!visiblelineset.Contains(sd.Line) || !refreshed.Add(sd)
					|| !VisualSectorExists(sd.Sector)) continue;
				BaseVisualSector vs = GetBaseVisualSector(sd.Sector);
				if(!visiblesectors.Contains(vs)) continue;
				vs.RefreshSidedefGeometry(sd);
				refreshedsectors.Add(vs);
			}
			RefreshVisibleSectorGeometry(refreshedsectors);

			if(refreshed.Count > 0)
			{
				InvalidatePickTarget(true);
				General.Interface.RedrawDisplay();
			}
		}

		internal void UpdateChangedFlatSurfaces(IEnumerable<Sector> changes)
		{
			HashSet<Sector> changedcontrols = new HashSet<Sector>();
			foreach(Sector sector in changes)
			{
				if(sector != null && !sector.IsDisposed) changedcontrols.Add(sector);
			}
			if(changedcontrols.Count == 0) return;

			HashSet<BaseVisualGeometrySector> refreshed = new HashSet<BaseVisualGeometrySector>();
			foreach(VisualSector visualsector in allsectors.Values)
			{
				BaseVisualSector sector = visualsector as BaseVisualSector;
				if(sector == null) continue;

				RefreshChangedFlatSurface(sector.Floor, changedcontrols, refreshed);
				RefreshChangedFlatSurface(sector.Ceiling, changedcontrols, refreshed);
				foreach(VisualFloor floor in sector.ExtraFloors)
					RefreshChangedFlatSurface(floor, changedcontrols, refreshed);
				foreach(VisualCeiling ceiling in sector.ExtraCeilings)
					RefreshChangedFlatSurface(ceiling, changedcontrols, refreshed);
				foreach(VisualFloor floor in sector.ExtraBackFloors)
					RefreshChangedFlatSurface(floor, changedcontrols, refreshed);
				foreach(VisualCeiling ceiling in sector.ExtraBackCeilings)
					RefreshChangedFlatSurface(ceiling, changedcontrols, refreshed);
			}

			// A floor or ceiling texture also controls whether bordering upper/lower
			// walls are rendered as sky. Rebuild only wall parts that already exist,
			// including the opposite side of each boundary. This makes F_SKY1-to-flat
			// changes (and undo/redo) visible immediately without dirtying or rebuilding
			// every visual sector in the map.
			HashSet<Sidedef> refreshedwalls = new HashSet<Sidedef>();
			foreach(Sector changed in changedcontrols)
			{
				foreach(Sidedef side in changed.Sidedefs)
				{
					RefreshChangedFlatWall(side, refreshedwalls);
					if(side.Other != null) RefreshChangedFlatWall(side.Other, refreshedwalls);
				}
			}

			if(refreshed.Count > 0)
				General.WriteLogLine("Refreshed " + refreshed.Count
					+ " Visual Mode floor/ceiling surfaces after texture undo/redo");
		}

		private void RefreshChangedFlatWall(Sidedef side, HashSet<Sidedef> refreshed)
		{
			if(side == null || side.IsDisposed || side.Sector == null || side.Sector.IsDisposed
				|| !refreshed.Add(side) || !VisualSectorExists(side.Sector)) return;

			BaseVisualSector visualsector = GetBaseVisualSector(side.Sector);
			if(visualsector.SidedefGeometryReady(side))
				visualsector.GetSidedefParts(side).SetupAllParts();
		}

		private static void RefreshChangedFlatSurface(BaseVisualGeometrySector surface,
			HashSet<Sector> changedcontrols, HashSet<BaseVisualGeometrySector> refreshed)
		{
			if(surface == null || !changedcontrols.Contains(surface.GetControlSector())
				|| !refreshed.Add(surface)) return;

			// Setup updates UVs in place and leaves the visual sector resident. This avoids
			// exposing empty geometry while an otherwise texture-only undo is processed.
			surface.Setup(surface.Level, surface.ExtraFloor);
		}

		private int QueueAlignedSidedef(Sidedef sd)
		{
			if(sd == null || sd.IsDisposed || !VisualSectorExists(sd.Sector)) return 0;
			BaseVisualSector vs = GetBaseVisualSector(sd.Sector);
			// Unseen walls will be built later from their already-updated map
			// offsets. Refresh only geometry that currently exists, avoiding a
			// second synchronous build wave after a large alignment operation.
			if(!vs.SidedefGeometryReady(sd) || !pendingalignedsidedefset.Add(sd)) return 0;
			pendingalignedsidedefs.Enqueue(sd);
			return 1;
		}

		private void ClearPendingRetainedVisuals()
		{
			pendingretainedsectors.Clear();
			pendingretainedsectorset.Clear();
			pendingretainedthings.Clear();
			pendingretainedthingset.Clear();
			retainedrefreshactive = false;
			retainedrefreshinitialsectors = 0;
			retainedrefreshinitialthings = 0;
		}

		private void RestorePendingSelection()
		{
			if(pendingselectionrestore.Count == 0) return;

			MapElement3D[] elements = pendingselectionrestore.ToArray();
			pendingselectionrestore.Clear();
			foreach(MapElement3D element in elements)
			{
				if(element == null || element.IsDisposed) continue;
				IVisualEventReceiver visualobject = GetObjectByMapElement3D(element);
				if(visualobject != null && !visualobject.Selected)
				{
					visualobject.OnSelectBegin();
					visualobject.OnSelectEnd();
				}
			}
		}

		private void ProcessPendingRetainedVisuals()
		{
			if(!retainedrefreshactive) return;

			long start = Clock.CurrentTime;
			int rebuiltsectors = 0;
			int rebuiltthings = 0;

			// Rebuild a few complete sectors atomically. Until a sector reaches this
			// point its previous geometry stays resident and drawable; after Rebuild,
			// the following cull swaps the visible lists to the replacement objects.
			while(pendingretainedsectors.Count > 0
				&& rebuiltsectors < RETAINED_REFRESH_MAX_SECTORS_PER_FRAME
				&& (rebuiltsectors == 0 || Clock.CurrentTime - start < RETAINED_REFRESH_BUDGET_MS))
			{
				Sector sector = pendingretainedsectors.Dequeue();
				if(RebuildPendingRetainedSector(sector)) rebuiltsectors++;
			}

			// Geometry has priority. Once it is caught up, refresh changed sprites and
			// models with whatever remains of the same small frame allowance.
			while(pendingretainedsectors.Count == 0 && pendingretainedthings.Count > 0
				&& (rebuiltsectors + rebuiltthings == 0
					|| Clock.CurrentTime - start < RETAINED_REFRESH_BUDGET_MS))
			{
				Thing thing = pendingretainedthings.Dequeue();
				pendingretainedthingset.Remove(thing);
				if(thing == null || thing.IsDisposed) continue;

				VisualThing visualthing;
				if(!allthings.TryGetValue(thing, out visualthing) || !(visualthing is BaseVisualThing)) continue;
				((BaseVisualThing)visualthing).Rebuild();
				rebuiltthings++;
			}

			if(rebuiltsectors > 0 || rebuiltthings > 0)
				DoCulling();

			if(pendingretainedsectors.Count == 0 && pendingretainedthings.Count == 0)
			{
				renderer.SetEventLines(General.Map.GetVisualEventLines(blockmap));
				PickTarget();
				RestorePendingSelection();
				General.WriteLogLine("Visual Mode retained refresh completed "
					+ retainedrefreshinitialsectors + " sectors and "
					+ retainedrefreshinitialthings + " things in "
					+ (Clock.CurrentTime - retainedrefreshstart) + " ms without removing live geometry");
				ClearPendingRetainedVisuals();
			}
		}

		private void ProcessPendingAlignedSidedefs()
		{
			long start = Clock.CurrentTime;
			int refreshed = 0;
			HashSet<VisualSector> refreshedsectors = new HashSet<VisualSector>();

			while(pendingalignedsidedefs.Count > 0
				&& (refreshed == 0 || Clock.CurrentTime - start < ALIGNMENT_REFRESH_BUDGET_MS))
			{
				Sidedef sd = pendingalignedsidedefs.Dequeue();
				pendingalignedsidedefset.Remove(sd);
				if(sd == null || sd.IsDisposed || !VisualSectorExists(sd.Sector)) continue;

				BaseVisualSector vs = GetBaseVisualSector(sd.Sector);
				if(!vs.SidedefGeometryReady(sd)) continue;
				vs.RefreshSidedefGeometry(sd);
				refreshedsectors.Add(vs);
				refreshed++;
			}

			if(refreshed > 0)
			{
				RefreshVisibleSectorGeometry(refreshedsectors);
				InvalidatePickTarget(false);
			}

			if(pendingalignedsidedefs.Count == 0 && alignmentrefreshactive)
			{
				General.WriteLogLine("Visual Mode texture-alignment refresh completed in "
					+ (Clock.CurrentTime - alignmentrefreshstart) + " ms");
				alignmentrefreshactive = false;
			}
		}

		//mxd. Moved here from Tools
		// This performs texture alignment along all walls that match with the same texture
		// NOTE: This method uses the sidedefs marking to indicate which sides have been aligned
		// When resetsidemarks is set to true, all sidedefs will first be marked false (not aligned).
		// Setting resetsidemarks to false is usefull to align only within a specific selection
		// (set the marked property to true for the sidedefs outside the selection)
		private void AutoAlignTextures(BaseVisualGeometrySidedef start, ImageData texture, bool alignx, bool aligny, bool resetsidemarks, HashSet<Sidedef> changes)
		{
			Stack<SidedefAlignJob> todo = new Stack<SidedefAlignJob>(50);
			double scalex = (General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.x : 1.0f;
			double scaley = (General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.y : 1.0f;

			// Mark all sidedefs false (they will be marked true when the texture is aligned).
			if(resetsidemarks) General.Map.Map.ClearMarkedSidedefs(false);

			// Begin with first sidedef
			SidedefAlignJob first = new SidedefAlignJob();
			first.sidedef = start.Sidedef;
			first.offsetx = start.Sidedef.OffsetX;
			int ystartalign = start.Sidedef.OffsetY; //mxd

			//mxd
			if(start.GeometryType == VisualGeometryType.WALL_MIDDLE_3D) 
			{
				first.controlSide = start.GetControlLinedef().Front;
				//first.offsetx += first.controlSide.OffsetX;
				//ystartalign += first.controlSide.OffsetY;
				ystartalign = first.controlSide.OffsetY;
			} 
			else 
			{
				first.controlSide = start.Sidedef;
			}

			// We potentially need to deal with 2 textures (because of long and short texture names). This is even important
			// for classic texture alignments, since for example Eternity Engine doesn't support local sidedef texture offsets,
			// but full texture names from a /textures directory
			HashSet<long> texturehashes = new HashSet<long> { texture.LongName };
			switch (start.GeometryType)
			{
				case VisualGeometryType.WALL_LOWER:
					texturehashes.Add(first.controlSide.LongLowTexture);
					break;

				case VisualGeometryType.WALL_MIDDLE:
				case VisualGeometryType.WALL_MIDDLE_3D:
					texturehashes.Add(first.controlSide.LongMiddleTexture);
					break;

				case VisualGeometryType.WALL_UPPER:
					texturehashes.Add(first.controlSide.LongHighTexture);
					break;
			}

			first.forward = true;
			todo.Push(first);

			// Continue until nothing more to align
			while(todo.Count > 0) 
			{
				// Get the align job to do
				SidedefAlignJob j = todo.Pop();

				// Make sure to not align already aligned textures. This prevents unexpected
				// results when aligning textures on circular shapes
				if (j.sidedef.Marked)
					continue;

				if(j.forward)
				{
					changes.Add(j.sidedef);
					changes.Add(j.controlSide);

					// Apply alignment
					// SRB2 flips the usage of control & in-level lines' sides
					//if(alignx) j.controlSide.OffsetX = (int)j.offsetx;
					//if(aligny) j.sidedef.OffsetY = (int)Math.Round((first.ceilingHeight - j.ceilingHeight) / scaley) + ystartalign;
					if (alignx) j.sidedef.OffsetX = (int)j.offsetx;
					if (aligny) j.controlSide.OffsetY = (int)Math.Round((first.ceilingHeight - j.ceilingHeight) / scaley) + ystartalign;
					int forwardoffset = (int)j.offsetx + (int)Math.Round(j.sidedef.Line.Length / scalex);
					int backwardoffset = (int)j.offsetx;

					j.sidedef.Marked = true;

					// Wrap the value within the width of the texture (to prevent ridiculous values)
					// NOTE: We don't use ScaledWidth here because the texture offset is in pixels, not mappixels
					if(texture.IsImageLoaded && BuilderModesTools.SidedefTextureMatch(this, j.sidedef, texturehashes)) 
					{
						if(alignx) j.sidedef.OffsetX %= texture.Width;
						//if(aligny) j.sidedef.OffsetY %= texture.Height;
						if(aligny) j.controlSide.OffsetY %= texture.Height; // SRB2, see above
					}

					// Add sidedefs backward (connected to the left vertex)
					Vertex v = j.sidedef.IsFront ? j.sidedef.Line.Start : j.sidedef.Line.End;
					AddSidedefsForAlignment(todo, v, false, backwardoffset, 1.0f, texturehashes, false, null);
					// Add sidedefs forward (connected to the right vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.End : j.sidedef.Line.Start;
					AddSidedefsForAlignment(todo, v, true, forwardoffset, 1.0f, texturehashes, false, null);
				}
				else
				{
					changes.Add(j.sidedef);
					changes.Add(j.controlSide);

					// Apply alignment
					// SRB2 flips the usage of control & in-level lines' sides
					//if(alignx) j.controlSide.OffsetX = (int)j.offsetx - (int)Math.Round(j.sidedef.Line.Length / scalex);
					//if(aligny) j.sidedef.OffsetY = (int)Math.Round((first.ceilingHeight - j.ceilingHeight) / scaley) + ystartalign;
					if (alignx) j.sidedef.OffsetX = (int)j.offsetx - (int)Math.Round(j.sidedef.Line.Length / scalex);
					if (aligny) j.controlSide.OffsetY = (int)Math.Round((first.ceilingHeight - j.ceilingHeight) / scaley) + ystartalign;
					int forwardoffset = (int)j.offsetx;
					int backwardoffset = (int)j.offsetx - (int)Math.Round(j.sidedef.Line.Length / scalex);

					j.sidedef.Marked = true;

					// Wrap the value within the width of the texture (to prevent ridiculous values)
					// NOTE: We don't use ScaledWidth here because the texture offset is in pixels, not mappixels
					if(texture.IsImageLoaded && BuilderModesTools.SidedefTextureMatch(this, j.sidedef, texturehashes)) 
					{
						if(alignx) j.sidedef.OffsetX %= texture.Width;
						//if(aligny) j.sidedef.OffsetY %= texture.Height;
						if (aligny) j.controlSide.OffsetY %= texture.Height; // SRB2, see above
					}

					// Add sidedefs forward (connected to the right vertex)
					Vertex v = j.sidedef.IsFront ? j.sidedef.Line.End : j.sidedef.Line.Start;
					AddSidedefsForAlignment(todo, v, true, forwardoffset, 1.0f, texturehashes, false, null);

					// Add sidedefs backward (connected to the left vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.Start : j.sidedef.Line.End;
					AddSidedefsForAlignment(todo, v, false, backwardoffset, 1.0f, texturehashes, false, null);
				}
			}
		}

		//mxd. Moved here from GZDoomEditing plugin
		// This performs UDMF texture alignment along all walls that match with the same texture
		// NOTE: This method uses the sidedefs marking to indicate which sides have been aligned
		// When resetsidemarks is set to true, all sidedefs will first be marked false (not aligned).
		// Setting resetsidemarks to false is usefull to align only within a specific selection
		// (set the marked property to true for the sidedefs outside the selection)
		private void AutoAlignTexturesUDMF(BaseVisualGeometrySidedef start, ImageData texture, bool alignx, bool aligny, bool resetsidemarks, bool checkselectedsidedefparts, HashSet<Sidedef> changes)
		{
			// Keep selection at wall-part precision. A normal wall and an FOF can occupy
			// the same map sidedef, so sidedef marking alone cannot tell which one the
			// user selected. Mixed alignment is enabled only when both surface types with
			// this texture were explicitly selected.
			HashSet<long> selectedVisualSideParts = new HashSet<long>();
			BaseVisualGeometrySidedef ordinaryreference = null;
			bool selectedordinary = false;
			bool selectedfof = false;
			if(checkselectedsidedefparts && !singleselection)
			{
				foreach(IVisualEventReceiver receiver in selectedobjects)
				{
					BaseVisualGeometrySidedef side = receiver as BaseVisualGeometrySidedef;
					if(side == null) continue;
					selectedVisualSideParts.Add(GetVisualSidePartKey(side));
					if(side.Texture == null || texture == null || side.Texture.LongName != texture.LongName) continue;

					if(side.GeometryType == VisualGeometryType.WALL_MIDDLE_3D)
						selectedfof = true;
					else
					{
						selectedordinary = true;
						if(ordinaryreference == null) ordinaryreference = side;
					}
				}
			}

			bool allowmixedselectedalignment = selectedordinary && selectedfof;
			if(allowmixedselectedalignment && start.GeometryType == VisualGeometryType.WALL_MIDDLE_3D
				&& ordinaryreference != null)
			{
				// The ordinary wall is the reference; the selected FOF receives its alignment.
				start = ordinaryreference;
				texture = start.Texture;
			}

			HashSet<long> alignedsides = new HashSet<long>(100);
			// Mark all sidedefs false (they will be marked true when the texture is aligned)
			if(resetsidemarks) General.Map.Map.ClearMarkedSidedefs(false);
			if(!texture.IsImageLoaded) return;

			bool worldpanning = texture.WorldPanning || General.Map.Data.MapInfo.ForceWorldPanning;

			Stack<SidedefAlignJob> todo = new Stack<SidedefAlignJob>(50);
			double scalex = (General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.x : 1.0f;
			double scaley = (General.Map.Config.ScaledTextureOffsets && !texture.WorldPanning) ? texture.Scale.y : 1.0f;

			SidedefAlignJob first = new SidedefAlignJob { sidedef = start.Sidedef, offsetx = start.Sidedef.OffsetX };
			first.controlSide = (start.GeometryType == VisualGeometryType.WALL_MIDDLE_3D ? start.GetControlLinedef().Front : start.Sidedef);
			bool aligning3dfloor = start.GeometryType == VisualGeometryType.WALL_MIDDLE_3D;
			Sidedef alignmentcontrolside = aligning3dfloor ? first.controlSide : null;

			//mxd. We potentially need to deal with 2 textures (because of long and short texture names)...
			HashSet<long> texturehashes = new HashSet<long> { texture.LongName };
			switch(start.GeometryType)
			{
				case VisualGeometryType.WALL_LOWER:
					texturehashes.Add(first.controlSide.LongLowTexture);
					break;

				case VisualGeometryType.WALL_MIDDLE:
				case VisualGeometryType.WALL_MIDDLE_3D:
					texturehashes.Add(first.controlSide.LongMiddleTexture);
					break;

				case VisualGeometryType.WALL_UPPER:
					texturehashes.Add(first.controlSide.LongHighTexture);
					break;
			}

			//mxd. Scale
			switch(start.GeometryType) 
			{
				case VisualGeometryType.WALL_UPPER:
					first.scaleX = start.Sidedef.Fields.GetValue("scalex_top", 1.0);
					first.scaleY = start.Sidedef.Fields.GetValue("scaley_top", 1.0);
					break;
				case VisualGeometryType.WALL_MIDDLE:
				case VisualGeometryType.WALL_MIDDLE_3D:
					first.scaleX = first.controlSide.Fields.GetValue("scalex_mid", 1.0);
					first.scaleY = first.controlSide.Fields.GetValue("scaley_mid", 1.0);
					break;
				case VisualGeometryType.WALL_LOWER:
					first.scaleX = start.Sidedef.Fields.GetValue("scalex_bottom", 1.0);
					first.scaleY = start.Sidedef.Fields.GetValue("scaley_bottom", 1.0);
					break;
			}
			// biwa
			double vwidth = worldpanning ? texture.ScaledWidth / first.scaleX : texture.Width;
			double vheight = worldpanning ? texture.ScaledHeight / first.scaleY : texture.Height;

			// Determine the Y alignment
			double ystartalign = start.Sidedef.OffsetY;
			switch(start.GeometryType) 
			{
				case VisualGeometryType.WALL_UPPER:
					ystartalign += Tools.GetSidedefTopOffsetY(start.Sidedef, start.Sidedef.Fields.GetValue("offsety_top", 0.0), worldpanning ? 1.0 : first.scaleY / scaley, false);//mxd
					break;
				case VisualGeometryType.WALL_MIDDLE:
					ystartalign += Tools.GetSidedefMiddleOffsetY(start.Sidedef, start.Sidedef.Fields.GetValue("offsety_mid", 0.0), worldpanning ? 1.0 : first.scaleY / scaley, false);//mxd
					break;
				case VisualGeometryType.WALL_MIDDLE_3D: //mxd. 3d-floors are not affected by Lower/Upper unpegged flags
					// SRB2 takes an FOF side's Y offset only from its control side.
					// Keep the target wall's normal middle-texture Y fields independent.
					ystartalign = first.controlSide.OffsetY - (start.Sidedef.Sector.CeilHeight - first.ceilingHeight);
					ystartalign += first.controlSide.Fields.GetValue("offsety_mid", 0.0);
					break;
				case VisualGeometryType.WALL_LOWER:
					ystartalign += Tools.GetSidedefBottomOffsetY(start.Sidedef, start.Sidedef.Fields.GetValue("offsety_bottom", 0.0), worldpanning ? 1.0 : first.scaleY / scaley, false);//mxd
					break;
			}

			// Begin with first sidedef
			switch(start.GeometryType) 
			{
				case VisualGeometryType.WALL_UPPER:
					first.offsetx += start.Sidedef.Fields.GetValue("offsetx_top", 0.0);
					break;
				case VisualGeometryType.WALL_MIDDLE:
					first.offsetx += start.Sidedef.Fields.GetValue("offsetx_mid", 0.0);
					break;
				case VisualGeometryType.WALL_MIDDLE_3D:
					// SRB2 adds the FOF control side's local X contribution to the
					// target wall's normal middle X offset. Its classic X offset is
					// deliberately not part of the thick-side formula.
					first.offsetx += start.Sidedef.Fields.GetValue("offsetx_mid", 0.0);
					first.offsetx += first.controlSide.Fields.GetValue("offsetx_mid", 0.0);
					break;
				case VisualGeometryType.WALL_LOWER:
					first.offsetx += start.Sidedef.Fields.GetValue("offsetx_bottom", 0.0);
					break;
			}
			first.forward = true;
			todo.Push(first);

			// A selected FOF can share the exact target sidedef with the ordinary
			// reference wall. Traversing connected vertices would never discover that
			// alternate surface, so seed its control-side jobs explicitly. Exact surface
			// selection below filters out every unselected FOF in the stack.
			if(allowmixedselectedalignment)
			{
				foreach(Sidedef controlside in GetControlSides(first.sidedef, true))
				{
					if(controlside == first.sidedef) continue;
					SidedefAlignJob alternate = new SidedefAlignJob();
					alternate.sidedef = first.sidedef;
					alternate.controlSide = controlside;
					alternate.offsetx = first.offsetx;
					alternate.scaleY = first.scaleY;
					alternate.forward = first.forward;
					todo.Push(alternate);
				}
			}

			// Continue until nothing more to align
			while(todo.Count > 0) 
			{
				Vertex v;
				double forwardoffset, backwardoffset;
				bool matchtop = false;
				bool matchmid = false;
				bool matchbottom = false;

				// Get the align job to do
				SidedefAlignJob j = todo.Pop();
				bool jobis3dfloor = j.controlSide != j.sidedef;

				// Make sure that each combination of sidedef and control side is only aligned once. 
				// This prevents unexpected results when aligning textures on circular shapes
				long checksum = (long)j.sidedef.Index << 32 | (long)j.controlSide.Index;
				if (alignedsides.Contains(checksum))
					continue;
				else
					alignedsides.Add(checksum);

				//mxd. Get visual parts
				if (VisualSectorExists(j.sidedef.Sector))
				{
					BaseVisualSector visualsector = GetBaseVisualSector(j.sidedef.Sector);
					visualsector.PrepareSidedefGeometry(j.sidedef);
					VisualSidedefParts parts = visualsector.GetSidedefParts(j.sidedef);
					//VisualSidedefParts controlparts = (j.sidedef != j.controlSide ? GetBaseVisualSector(j.controlSide.Sector).GetSidedefParts(j.controlSide) : parts);

					matchtop = (!j.sidedef.Marked && (!singleselection || texturehashes.Contains(j.sidedef.LongHighTexture)) && (parts.upper != null && parts.upper.Triangles > 0));
					matchbottom = (!j.sidedef.Marked && (!singleselection || texturehashes.Contains(j.sidedef.LongLowTexture)) && (parts.lower != null && parts.lower.Triangles > 0));
					if(aligning3dfloor || (allowmixedselectedalignment && jobis3dfloor))
					{
						matchtop = false;
						matchbottom = false;

						// A target sidedef can host several stacked FOF walls as well as a
						// normal midtexture. Match the exact control linedef that the user
						// aimed at instead of treating every middle surface as one part.
						if(parts.middle3d != null)
						{
							foreach(VisualMiddle3D vm3d in parts.middle3d)
							{
								Linedef controlline = vm3d.GetControlLinedef();
								if(controlline != null && controlline.Front == j.controlSide
									&& vm3d.Triangles > 0 && vm3d.Texture != null
									&& texturehashes.Contains(vm3d.Texture.LongName))
								{
									matchmid = true;
									break;
								}
							}
						}
					}
					else
					{
						// Normal midtextures remain independent from any FOFs rendered on
						// the same target sidedef, even when they use the same image.
						matchmid = ((!singleselection || texturehashes.Contains(j.sidedef.LongMiddleTexture))
							&& ((parts.middledouble != null && parts.middledouble.Triangles > 0)
								|| (parts.middlesingle != null && parts.middlesingle.Triangles > 0)));
					}


					//mxd. If there's a selection, check if matched part is actually selected
					if(checkselectedsidedefparts && !singleselection)
					{
						if(matchtop) matchtop = parts.upper.Selected;
						if(matchbottom) matchbottom = parts.lower.Selected;
						if(matchmid)
						{
							if(jobis3dfloor)
								matchmid = selectedVisualSideParts.Contains(GetVisualSidePartKey(j.sidedef, VisualGeometryType.WALL_MIDDLE_3D, j.controlSide));
							else
								matchmid = ((parts.middledouble != null && parts.middledouble.Selected)
									|| (parts.middlesingle != null && parts.middlesingle.Selected));
						}
					}
				}

				if(!matchbottom && !matchtop && !matchmid) continue; //mxd

				//mxd. We want to skip realigning of the starting wall part
				if(matchtop) matchtop = (j.sidedef != start.Sidedef || start.GeometryType != VisualGeometryType.WALL_UPPER);
				if(matchmid)
				{
					bool startingpart = j.sidedef == start.Sidedef
						&& ((start.GeometryType == VisualGeometryType.WALL_MIDDLE_3D
							&& jobis3dfloor && j.controlSide == first.controlSide)
							|| (start.GeometryType != VisualGeometryType.WALL_MIDDLE_3D && !jobis3dfloor));
					matchmid = !startingpart;
				}
				if(matchbottom) matchbottom = (j.sidedef != start.Sidedef || start.GeometryType != VisualGeometryType.WALL_LOWER);

				if(matchbottom || matchtop || matchmid)
				{
					changes.Add(j.sidedef);
					changes.Add(j.controlSide);
					j.sidedef.Fields.BeforeFieldsChange();
					if(j.sidedef.Index != j.controlSide.Index) j.controlSide.Fields.BeforeFieldsChange(); //mxd
				}
				
				//mxd. Apply Scale
				if(matchtop)
				{
					UniFields.SetFloat(j.sidedef.Fields, "scalex_top", first.scaleX, 1.0);
					UniFields.SetFloat(j.sidedef.Fields, "scaley_top", j.scaleY, 1.0);
				}
				if(matchmid)
				{
					UniFields.SetFloat(j.controlSide.Fields, "scalex_mid", first.scaleX, 1.0);
					UniFields.SetFloat(j.controlSide.Fields, "scaley_mid", j.scaleY, 1.0);
				}
				if(matchbottom)
				{
					UniFields.SetFloat(j.sidedef.Fields, "scalex_bottom", first.scaleX, 1.0);
					UniFields.SetFloat(j.sidedef.Fields, "scaley_bottom", j.scaleY, 1.0);
				}

				if(j.forward) 
				{
					// Apply alignment
					if(alignx)
					{
						double offset = j.offsetx;
						offset -= j.sidedef.OffsetX;

						if(matchtop)
						{
							j.sidedef.Fields["offsetx_top"] = new UniValue(UniversalType.Float, Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
						}
						if(matchbottom)
						{
							j.sidedef.Fields["offsetx_bottom"] = new UniValue(UniversalType.Float, Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
						}
						if(matchmid) 
						{
							if(jobis3dfloor)
							{
								// Preserve any ordinary middle texture on the target wall. The
								// remaining value is SRB2's separate FOF control contribution.
								offset -= j.sidedef.Fields.GetValue("offsetx_mid", 0.0);
								j.controlSide.Fields["offsetx_mid"] = new UniValue(UniversalType.Float,
									Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
							}
							else
							{
								j.sidedef.Fields["offsetx_mid"] = new UniValue(UniversalType.Float,
									Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
							}
						}
					}

					if(aligny) 
					{
						double offset;

						if (!worldpanning)
							offset = ((start.Sidedef.Sector.CeilHeight - j.ceilingHeight) / scaley) * Math.Abs(j.scaleY)  + ystartalign - j.sidedef.OffsetY; //mxd
						else
							offset = (start.Sidedef.Sector.CeilHeight - j.ceilingHeight + ystartalign - j.sidedef.OffsetY);

						if (matchtop)
						{
							double scale = !worldpanning ? j.scaleY / scaley : 1.0f;

							j.sidedef.Fields["offsety_top"] = new UniValue(UniversalType.Float,
								Math.Round(Tools.GetSidedefTopOffsetY(j.sidedef, offset, scale, true) % vheight, General.Map.FormatInterface.VertexDecimals)); //mxd

						}
						if (matchbottom)
						{
							double scale = !worldpanning ? j.scaleY / scaley : 1.0f;

							j.sidedef.Fields["offsety_bottom"] = new UniValue(UniversalType.Float,
								Math.Round(Tools.GetSidedefBottomOffsetY(j.sidedef, offset, scale, true) % vheight, General.Map.FormatInterface.VertexDecimals)); //mxd
						}
						if(matchmid) 
						{
							//mxd. Side is part of a 3D floor?
							if(jobis3dfloor)
							{
								double controloffset = offset + j.sidedef.OffsetY - j.controlSide.OffsetY;
								j.controlSide.Fields["offsety_mid"] = new UniValue(UniversalType.Float,
									Math.Round(controloffset % vheight, General.Map.FormatInterface.VertexDecimals));
							} 
							else
							{
								ImageData tex = General.Map.Data.GetTextureImage(j.sidedef.LongMiddleTexture);
								double scale = !worldpanning ? j.scaleY / scaley : 1.0f;
								offset = Tools.GetSidedefMiddleOffsetY(j.sidedef, offset, scale, true);

								if (tex != null && tex.IsImageLoaded)
								{
									bool startisnonwrappedmidtex = (start.Sidedef.Other != null && start.GeometryType == VisualGeometryType.WALL_MIDDLE && !start.Sidedef.IsFlagSet("wrapmidtex") && !start.Sidedef.Line.IsFlagSet("wrapmidtex"));
									bool cursideisnonwrappedmidtex = (j.sidedef.Other != null && !j.sidedef.IsFlagSet("wrapmidtex") && !j.sidedef.Line.IsFlagSet("wrapmidtex"));
									
									//mxd. Only clamp when the texture is wrapped 
									if(!cursideisnonwrappedmidtex) offset %= vheight;

									if(!startisnonwrappedmidtex && cursideisnonwrappedmidtex)
									{
										//mxd. This should be doublesided non-wrapped line. Find the nearset aligned position
										double curoffset = UniFields.GetFloat(j.sidedef.Fields, "offsety_mid") + j.sidedef.OffsetY;
										offset += vheight * Math.Round(curoffset / vheight - 0.5f * Math.Sign(j.scaleY));

										// Make sure the surface stays between floor and ceiling
										if(j.sidedef.Line.IsFlagSet(General.Map.Config.PegMidtextureFlag) || Math.Sign(j.scaleY) == -1)
										{
											if(offset < -vheight)
												offset += vheight;
											else if(offset > j.sidedef.GetMiddleHeight())
												offset -= vheight;
										}
										else
										{
											if(offset < -(j.sidedef.GetMiddleHeight() + vheight))
												offset += vheight;
											else if(offset > vheight)
												offset -= vheight;
										}
									}
								}

								j.sidedef.Fields["offsety_mid"] = new UniValue(UniversalType.Float, 
									Math.Round(offset, General.Map.FormatInterface.VertexDecimals)); //mxd
							}
						}
					}

					backwardoffset = j.offsetx;

					if (!worldpanning)
					{
						if (texture is HiResImage)
							forwardoffset = j.offsetx + Math.Round((j.sidedef.Line.Length / scalex) % vwidth, General.Map.FormatInterface.VertexDecimals);
						else
							forwardoffset = j.offsetx + Math.Round((j.sidedef.Line.Length / scalex * Math.Abs(first.scaleX)) % vwidth, General.Map.FormatInterface.VertexDecimals);
					}
					else
						forwardoffset = Math.Round((j.offsetx + j.sidedef.Line.Length) % vwidth, General.Map.FormatInterface.VertexDecimals);

					// Done this sidedef
					j.sidedef.Marked = true;

					// Add sidedefs backward (connected to the left vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.Start : j.sidedef.Line.End;
					AddSidedefsForAlignment(todo, v, false, backwardoffset, j.scaleY, texturehashes, true, alignmentcontrolside, allowmixedselectedalignment);

					// Add sidedefs forward (connected to the right vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.End : j.sidedef.Line.Start;
					AddSidedefsForAlignment(todo, v, true, forwardoffset, j.scaleY, texturehashes, true, alignmentcontrolside, allowmixedselectedalignment);
				} 
				else // backward
				{
					// Apply alignment
					if(alignx) 
					{
						double offset;
						
						if(!worldpanning)
						{
							if (texture is HiResImage)
								offset = Math.Round((j.offsetx - j.sidedef.OffsetX - j.sidedef.Line.Length / scalex) % vwidth, General.Map.FormatInterface.VertexDecimals);
							else
								offset = Math.Round((j.offsetx - j.sidedef.OffsetX - j.sidedef.Line.Length / scalex * first.scaleX) % vwidth, General.Map.FormatInterface.VertexDecimals);
						}
						else
							offset = Math.Round((j.offsetx - j.sidedef.OffsetX - j.sidedef.Line.Length) % vwidth, General.Map.FormatInterface.VertexDecimals);

						if(matchtop)
						{
							j.sidedef.Fields["offsetx_top"] = new UniValue(UniversalType.Float,
								Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
						}
						if(matchbottom)
						{
							j.sidedef.Fields["offsetx_bottom"] = new UniValue(UniversalType.Float,
								Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
						}
						if(matchmid)
						{
							if(jobis3dfloor)
							{
								offset -= j.sidedef.Fields.GetValue("offsetx_mid", 0.0);
								j.controlSide.Fields["offsetx_mid"] = new UniValue(UniversalType.Float,
									Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
							}
							else
							{
								j.sidedef.Fields["offsetx_mid"] = new UniValue(UniversalType.Float,
									Math.Round(offset % vwidth, General.Map.FormatInterface.VertexDecimals));
							}
						}
					}

					if(aligny) 
					{
						double offset = ((start.Sidedef.Sector.CeilHeight - j.ceilingHeight) / scaley) * Math.Abs(j.scaleY) + ystartalign; //mxd
						offset -= j.sidedef.OffsetY; //mxd

						if(matchtop)
						{
							double scale = !worldpanning ? j.scaleY / scaley : 1.0f;

							j.sidedef.Fields["offsety_top"] = new UniValue(UniversalType.Float, 
								Math.Round(Tools.GetSidedefTopOffsetY(j.sidedef, offset, scale, true) % vheight, General.Map.FormatInterface.VertexDecimals)); //mxd
						}
						if(matchbottom)
						{
							double scale = !worldpanning ? j.scaleY / scaley : 1.0f;

							j.sidedef.Fields["offsety_bottom"] = new UniValue(UniversalType.Float,
								Math.Round(Tools.GetSidedefBottomOffsetY(j.sidedef, offset, scale, true) % vheight, General.Map.FormatInterface.VertexDecimals)); //mxd
						}
						if(matchmid) 
						{
							//mxd. Side is part of a 3D floor?
							if(jobis3dfloor)
							{
								double controloffset = offset + j.sidedef.OffsetY - j.controlSide.OffsetY;
								j.controlSide.Fields["offsety_mid"] = new UniValue(UniversalType.Float,
									Math.Round(controloffset % vheight, General.Map.FormatInterface.VertexDecimals));
							} 
							else 
							{
								ImageData tex = General.Map.Data.GetTextureImage(j.sidedef.LongMiddleTexture);
								double scale = !worldpanning ? j.scaleY / scaley : 1.0;
								offset = Tools.GetSidedefMiddleOffsetY(j.sidedef, offset, scale, true);

								if(tex != null && tex.IsImageLoaded)
								{
									bool startisnonwrappedmidtex = (start.Sidedef.Other != null && start.GeometryType == VisualGeometryType.WALL_MIDDLE && !start.Sidedef.IsFlagSet("wrapmidtex") && !start.Sidedef.Line.IsFlagSet("wrapmidtex"));
									bool cursideisnonwrappedmidtex = (j.sidedef.Other != null && !j.sidedef.IsFlagSet("wrapmidtex") && !j.sidedef.Line.IsFlagSet("wrapmidtex"));
									
									//mxd. Only clamp when the texture is wrapped 
									if(!cursideisnonwrappedmidtex) offset %= vheight;

									if(!startisnonwrappedmidtex && cursideisnonwrappedmidtex)
									{
										//mxd. This should be doublesided non-wrapped line. Find the nearset aligned position
										double curoffset = UniFields.GetFloat(j.sidedef.Fields, "offsety_mid") + j.sidedef.OffsetY;
										offset += tex.Height * Math.Round(curoffset / vheight - 0.5f * Math.Sign(j.scaleY));

										// Make sure the surface stays between floor and ceiling
										if(j.sidedef.Line.IsFlagSet(General.Map.Config.PegMidtextureFlag) || Math.Sign(j.scaleY) == -1)
										{
											if(offset < -vheight)
												offset += vheight;
											else if(offset > j.sidedef.GetMiddleHeight())
												offset -= vheight;
										}
										else
										{
											if(offset < -(j.sidedef.GetMiddleHeight() + vheight))
												offset += vheight;
											else if(offset > vheight)
												offset -= vheight;
										}
									}
								}

								j.sidedef.Fields["offsety_mid"] = new UniValue(UniversalType.Float, 
									Math.Round(offset, General.Map.FormatInterface.VertexDecimals)); //mxd
							}
						}
					}

					forwardoffset = j.offsetx;

					if (!worldpanning)
					{
						if (texture is HiResImage)
							backwardoffset = Math.Round((j.offsetx - j.sidedef.Line.Length / scalex) % vwidth, General.Map.FormatInterface.VertexDecimals);
						else
							backwardoffset = Math.Round((j.offsetx - j.sidedef.Line.Length / scalex * Math.Abs(first.scaleX)) % vwidth, General.Map.FormatInterface.VertexDecimals);
					}
					else
						backwardoffset = Math.Round((j.offsetx - j.sidedef.Line.Length) % vwidth, General.Map.FormatInterface.VertexDecimals);

					// Done this sidedef
					j.sidedef.Marked = true;

					// Add sidedefs forward (connected to the right vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.End : j.sidedef.Line.Start;
					AddSidedefsForAlignment(todo, v, true, forwardoffset, j.scaleY, texturehashes, true, alignmentcontrolside, allowmixedselectedalignment);

					// Add sidedefs backward (connected to the left vertex)
					v = j.sidedef.IsFront ? j.sidedef.Line.Start : j.sidedef.Line.End;
					AddSidedefsForAlignment(todo, v, false, backwardoffset, j.scaleY, texturehashes, true, alignmentcontrolside, allowmixedselectedalignment);
				}
			}
		}

		// This adds the matching, unmarked sidedefs from a vertex for texture alignment
		private void AddSidedefsForAlignment(Stack<SidedefAlignJob> stack, Vertex v, bool forward, double offsetx, double scaleY, HashSet<long> texturelongnames, bool udmf, Sidedef alignmentcontrolside, bool allowmixedselectedalignment = false)
		{
			foreach(Linedef ld in v.Linedefs)
			{
				Sidedef side1 = forward ? ld.Front : ld.Back;
				Sidedef side2 = forward ? ld.Back : ld.Front;

				// Preserve the original traversal rule: once either face of a line has
				// been handled, do not enter that line again from the opposite direction.
				if((side1 != null && side1.Marked) || (side2 != null && side2.Marked)) continue;

				if((ld.Start == v) && (side1 != null) && !side1.Marked)
				{
					List<Sidedef> controlSides = GetControlSides(side1, udmf); //mxd

					foreach(Sidedef s in controlSides)
					{
						// Normal middle textures must stay on the visible sidedef. When
						// aligning an FOF, however, follow every matching FOF control
						// side at the connected wall. Requiring the exact starting control
						// side prevented A from crossing into an adjacent FOF that uses a
						// different control line with the same texture.
						bool correctpart = allowmixedselectedalignment
							|| (alignmentcontrolside == null ? s == side1 : s != side1);
						if(correctpart && (!singleselection || BuilderModesTools.SidedefTextureMatch(this, s, texturelongnames)))
						{
							SidedefAlignJob nj = new SidedefAlignJob();
							nj.forward = forward;
							nj.offsetx = offsetx;
							nj.scaleY = scaleY; //mxd
							nj.sidedef = side1;
							nj.controlSide = s; //mxd
							stack.Push(nj);
						}
					}
				} 
				else if((ld.End == v) && (side2 != null) && !side2.Marked) 
				{
					List<Sidedef> controlSides = GetControlSides(side2, udmf); //mxd

					foreach(Sidedef s in controlSides) 
					{
						bool correctpart = allowmixedselectedalignment
							|| (alignmentcontrolside == null ? s == side2 : s != side2);
						if(correctpart && (!singleselection || BuilderModesTools.SidedefTextureMatch(this, s, texturelongnames)))
						{
							SidedefAlignJob nj = new SidedefAlignJob();
							nj.forward = forward;
							nj.offsetx = offsetx;
							nj.scaleY = scaleY; //mxd
							nj.sidedef = side2;
							nj.controlSide = s; //mxd
							stack.Push(nj);
						}
					}
				}
			}
		}

		//mxd
		private static long GetVisualSidePartKey(BaseVisualGeometrySidedef side)
		{
			Sidedef controlside = null;
			if(side.GeometryType == VisualGeometryType.WALL_MIDDLE_3D)
			{
				Linedef controlline = side.GetControlLinedef();
				if(controlline != null) controlside = controlline.Front;
			}
			return GetVisualSidePartKey(side.Sidedef, side.GeometryType, controlside);
		}

		private static long GetVisualSidePartKey(Sidedef side, VisualGeometryType geoType, Sidedef controlside)
		{
			// Positive low words identify individual FOF control sides. Negative low
			// words identify ordinary wall-part types, keeping both namespaces apart.
			int part = (geoType == VisualGeometryType.WALL_MIDDLE_3D && controlside != null)
				? controlside.Index : ~((int)geoType);
			return ((long)side.Index << 32) | (uint)part;
		}

		//mxd
		private List<Sidedef> GetControlSides(Sidedef side, bool udmf) 
		{
			if(side.Other == null) return new List<Sidedef> { side };
			if(side.Other.Sector.Tag == 0) return new List<Sidedef> { side };

			SectorData data = GetSectorDataEx(side.Other.Sector);
			if(data == null || data.ExtraFloors.Count == 0) return new List<Sidedef> { side };

			List<Sidedef> sides = new List<Sidedef>();
			foreach(Effect3DFloor ef in data.ExtraFloors)
				sides.Add(ef.Linedef.Front);

			if(udmf)
				sides.Add(side); //UDMF map format
			else
				sides.Insert(0, side); //Doom/Hexen map format: if a sidedef has lower/upper parts, they take predecence in alignment

			return sides;
		}

		#endregion
	}
}
