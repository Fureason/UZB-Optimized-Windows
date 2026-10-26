
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
using System.Drawing.Drawing2D;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Models;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.GZBuilder;
using ColorMap = System.Drawing.Imaging.ColorMap;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
	internal sealed class Renderer3D : Renderer, IRenderer3D
	{
		#region ================== Constants

		private const float PROJ_NEAR_PLANE = 1f;
		private const float FOG_RANGE = 0.9f;
        private const int MAX_DYNLIGHTS_PER_SURFACE = 64;

		#endregion

		#region ================== Variables

		// Matrices
		private Matrix projection;
		private Matrix inverseprojection;
		private Matrix view3d;
		private Matrix inverseview3d;
		private Matrix billboard;
		private Matrix view2d;
		private Matrix world;
		private Vector3D cameraposition;
        private Vector3D cameravector;
		private ShaderName shaderpass;

        // Window size
        private Size windowsize;
		
		// Frustum
		private ProjectedFrustum2D frustum;
		
		// Thing cage
		private bool renderthingcages;
		//mxd
		private VisualVertexHandle vertexhandle;
		private int[] lightOffsets;
		
		private Data.ColorMap classicLightingColorMap = null;
		private Texture classicLightingColorMapTex = null;
		
		// Slope handle
		private VisualSlopeHandle visualslopehandle;

		// Crosshair
		private FlatVertex[] crosshairverts;
		private bool crosshairbusy;
		private Texture skyboxrendertarget;
		private FlatVertex[] skyboxscreenverts;

		// Highlighting
		private IVisualPickable highlighted;
		private float highlightglow;
		private float highlightglowinv;
		private bool showselection;
		private bool showhighlight;
		private bool skyboxbackground;
		private bool skyboxrenderpass;
		
		//mxd. Solid geometry to be rendered. Must be sorted by sector.
		private Dictionary<ImageData, List<VisualGeometry>> solidgeo;

		//mxd. Masked geometry to be rendered. Must be sorted by sector.
		private Dictionary<ImageData, List<VisualGeometry>> maskedgeo;

		//mxd. Translucent geometry to be rendered. Must be sorted by camera distance.
		private List<VisualGeometry> translucentgeo;

		//mxd. Geometry to be rendered as skybox.
		private List<VisualGeometry> skygeo;

		//mxd. Solid things to be rendered (currently(?) there won't be any). Must be sorted by sector.
		private Dictionary<ImageData, List<VisualThing>> solidthings;

		//mxd. Masked things to be rendered. Must be sorted by sector.
		private Dictionary<ImageData, List<VisualThing>> maskedthings;

		//mxd. Translucent things to be rendered. Must be sorted by camera distance.
		private List<VisualThing> translucentthings;

		//mxd. Things with attached dynamic lights
		private List<VisualThing> lightthings;
		
		//mxd. Things, which should be rendered as models
		private Dictionary<ModelData, List<VisualThing>> maskedmodelthings;

		//mxd. Things, which should be rendered as translucent models
		private List<VisualThing> translucentmodelthings;
		private List<VisualThing> coronathings;

		//mxd. All things. Used to render thing cages
		private List<VisualThing> allthings;

		// Per-frame render groups are recycled. Visual mode can submit hundreds of
		// frames per second, so allocating a new dictionary and a new list for every
		// texture on every frame caused regular generation-0/1 GC pauses on big maps.
		private Stack<List<VisualGeometry>> geometrygrouppool;
		private Stack<List<VisualThing>> thinggrouppool;
		private List<VisualThing> opaquemodelthings;
		private HashSet<VisualSector> preparedgeometrysectors;

		// Dynamic-light shader data is reused by each sequential render pass. These
		// used to be recreated three times per frame (15 arrays total).
		private Vector4f[] lightcolors;
		private Vector4f[] lightpositionsandradii;
		private Vector4f[] lightorientations;
		private Vector2f[] lightspotradii;
		private Vector2f[] lightstrengthsandlinearities;

		// Prepared once after the visible-light list has been culled. The same light
		// can touch hundreds of surfaces, so recalculating its position, colour,
		// spotlight trigonometry and attenuation data for every surface was one of
		// the largest CPU costs in light-heavy Visual Mode scenes.
		private Vector4f[] preparedlightcolors;
		private Vector4f[] preparedlightpositionsandradii;
		private Vector4f[] preparedlightorientations;
		private Vector2f[] preparedlightspotradii;
		private Vector2f[] preparedlightstrengthsandlinearities;
		private int preparedlightcount;
		private int[] gatheredlightsources;
		private int[] previousgatheredlightsources;
		private int previousgatheredlightcount;

		//mxd. Visual vertices
		private List<VisualVertex> visualvertices;

		// Visual slope handles
		private ICollection<VisualSlope> visualslopehandles;

		//mxd. Event lines
		private List<Line3D> eventlines;
		private VertexBuffer eventlinebuffer;
		private int eventlineprimitivecount;
		private bool eventlinesdirty;

        // FPS-related
        private int fps = 0;
		private int fpssamplecount;
        private System.Diagnostics.Stopwatch fpsWatch;
        private TextLabel fpsLabel;
		private long renderframestart;
		private int adaptiveuploadcount;
		private long frametimingwindowstart;
		private double frametimingcputotal;
		private double frametimingpresenttotal;
		private int frametimingcount;
		private bool highfpsvsyncfallbackattempted;
		private double lastaveragecpumilliseconds;
		private double lastaveragepresentmilliseconds;

		// OnProcess and OnRedrawDisplay both submit the normal camera each frame.
		// Reuse its matrices and frustum until the actual view (or a setting that
		// affects the frustum) changes. The skybox camera naturally invalidates this
		// comparison while its separate pass is being rendered.
		private bool hascachedview;
		private Vector3D cachedviewposition;
		private Vector3D cachedviewtarget;
		private float cachedviewdistance;
		private int cachedviewfov;

        #endregion

        #region ================== Properties

        public ProjectedFrustum2D Frustum2D { get { return frustum; } }
		public bool DrawThingCages { get { return renderthingcages; } set { renderthingcages = value; } }
		public bool ShowSelection { get { return showselection; } set { showselection = value; } }
		public bool ShowHighlight { get { return showhighlight; } set { showhighlight = value; } }
		public bool SkyboxBackground { get { return skyboxbackground; } set { skyboxbackground = value; } }
		
		private bool UseIndexedTexture
		{
			get { return General.Settings.ClassicRendering && !fullbrightness; }
		}
		
		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		internal Renderer3D(RenderDevice graphics) : base(graphics)
		{
			// Initialize
			//CreateProjection(); // [ZZ] don't do undefined things once not even ready
			CreateMatrices2D();
			renderthingcages = true;
			showselection = true;
			showhighlight = true;
			eventlines = new List<Line3D>(); //mxd
			eventlinesdirty = true;

			solidgeo = new Dictionary<ImageData, List<VisualGeometry>>(64);
			maskedgeo = new Dictionary<ImageData, List<VisualGeometry>>(32);
			translucentgeo = new List<VisualGeometry>(64);
			skygeo = new List<VisualGeometry>(16);
			solidthings = new Dictionary<ImageData, List<VisualThing>>(16);
			maskedthings = new Dictionary<ImageData, List<VisualThing>>(64);
			translucentthings = new List<VisualThing>(32);
			maskedmodelthings = new Dictionary<ModelData, List<VisualThing>>(8);
			translucentmodelthings = new List<VisualThing>(8);
			coronathings = new List<VisualThing>(16);
			lightthings = new List<VisualThing>(32);
			allthings = new List<VisualThing>(128);
			geometrygrouppool = new Stack<List<VisualGeometry>>(64);
			thinggrouppool = new Stack<List<VisualThing>>(64);
			opaquemodelthings = new List<VisualThing>(16);
			preparedgeometrysectors = new HashSet<VisualSector>();
			lightcolors = new Vector4f[MAX_DYNLIGHTS_PER_SURFACE];
			lightpositionsandradii = new Vector4f[MAX_DYNLIGHTS_PER_SURFACE];
			lightorientations = new Vector4f[MAX_DYNLIGHTS_PER_SURFACE];
			lightspotradii = new Vector2f[MAX_DYNLIGHTS_PER_SURFACE];
			lightstrengthsandlinearities = new Vector2f[MAX_DYNLIGHTS_PER_SURFACE];
			preparedlightcolors = new Vector4f[32];
			preparedlightpositionsandradii = new Vector4f[32];
			preparedlightorientations = new Vector4f[32];
			preparedlightspotradii = new Vector2f[32];
			preparedlightstrengthsandlinearities = new Vector2f[32];
			gatheredlightsources = new int[MAX_DYNLIGHTS_PER_SURFACE];
			previousgatheredlightsources = new int[MAX_DYNLIGHTS_PER_SURFACE];
			previousgatheredlightcount = -1;
			lightOffsets = new int[4];
			// Dummy frustum
			frustum = new ProjectedFrustum2D(new Vector2D(), 0.0f, 0.0f, PROJ_NEAR_PLANE,
				General.Settings.ViewDistance, (float)Angle2D.DegToRad(General.Settings.VisualFOV));

            fpsLabel = new TextLabel();
            fpsLabel.AlignX = TextAlignmentX.Left;
            fpsLabel.AlignY = TextAlignmentY.Top;
            fpsLabel.Text = "(FPS unavailable)";
            fpsWatch = new System.Diagnostics.Stopwatch();
			// Fill the first visible view quickly. The adaptive limiter backs off hard
			// when uploads become expensive, while a larger initial allowance avoids
			// showing black loading textures for many consecutive frames.
			adaptiveuploadcount = 16;
			frametimingwindowstart = System.Diagnostics.Stopwatch.GetTimestamp();

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
				if(vertexhandle != null) vertexhandle.Dispose(); //mxd
				if (visualslopehandle != null) visualslopehandle.Dispose();
				if(skyboxrendertarget != null) skyboxrendertarget.Dispose();
				if(eventlinebuffer != null) eventlinebuffer.Dispose();

				// Done
				base.Dispose();
			}
		}

		#endregion

		#region ================== View

		// This is apparently the only part of the codebase that needs a 4D-vector
		// so I'm not bothering with a full implementation :p
		private struct Vector4D
		{
			public double x;
			public double y;
			public double z;
			public double w;

			public Vector4D(double x, double y, double z, double w)
			{
				this.x = x;
				this.y = y;
				this.z = z;
				this.w = w;
			}

			public static Vector4D operator *(Matrix m, Vector4D v)
			{
				Vector4D r;

				r.x = m.M11 * v.x + m.M21 * v.y + m.M31 * v.z + m.M41 * v.w;
				r.y = m.M12 * v.x + m.M22 * v.y + m.M32 * v.z + m.M42 * v.w;
				r.z = m.M13 * v.x + m.M23 * v.y + m.M33 * v.z + m.M43 * v.w;
				r.w = m.M14 * v.x + m.M24 * v.y + m.M34 * v.z + m.M44 * v.w;

				return r;
			}
		}

		public Vector3D DisplayToWorld(Vector2D mousepos)
		{
			double mousex = (2.0 * mousepos.x / General.Map.Graphics.RenderTarget.ClientSize.Width) - 1.0;
			double mousey = 1.0 - (2.0 * mousepos.y / General.Map.Graphics.RenderTarget.ClientSize.Height);

			Vector4D pos4d = (inverseprojection * inverseview3d) * new Vector4D(mousex, mousey, 0.0, 1.0);
			Vector3D pos = new Vector3D(pos4d.x / pos4d.w, pos4d.y / pos4d.w, pos4d.z / pos4d.w);

			return (pos - cameraposition).GetNormal();
		}

		#endregion

		#region ================== Management

		// This is called before a device is reset
		// (when resized or display adapter was changed)
		public override void UnloadResource()
		{
			crosshairverts = null;
			if(eventlinebuffer != null)
			{
				eventlinebuffer.Dispose();
				eventlinebuffer = null;
			}
			eventlinesdirty = true;
		}
		
		// This is called resets when the device is reset
		// (when resized or display adapter was changed)
		public override void ReloadResource()
		{
			CreateMatrices2D();
		}

		// This makes screen vertices for display
		private void CreateCrosshairVerts(Size texturesize)
		{
			// Determine coordinates
			float width = windowsize.Width;
			float height = windowsize.Height;
			RectangleF rect = new RectangleF((float)Math.Round((width - texturesize.Width) * 0.5f), (float)Math.Round((height - texturesize.Height) * 0.5f), texturesize.Width, texturesize.Height);
			
			// Make vertices
			crosshairverts = new FlatVertex[4];
			crosshairverts[0].x = rect.Left;
			crosshairverts[0].y = rect.Top;
			crosshairverts[0].c = -1;
			crosshairverts[1].x = rect.Right;
			crosshairverts[1].y = rect.Top;
			crosshairverts[1].c = -1;
			crosshairverts[1].u = 1.0f;
			crosshairverts[2].x = rect.Left;
			crosshairverts[2].y = rect.Bottom;
			crosshairverts[2].c = -1;
			crosshairverts[2].v = 1.0f;
			crosshairverts[3].x = rect.Right;
			crosshairverts[3].y = rect.Bottom;
			crosshairverts[3].c = -1;
			crosshairverts[3].u = 1.0f;
			crosshairverts[3].v = 1.0f;
		}
		
		#endregion

		#region ================== Resources

		//mxd
		internal void UpdateVertexHandle()
		{
			if(vertexhandle != null)
			{
				vertexhandle.UnloadResource();
				vertexhandle.ReloadResource();
			}
		}

		internal void UpdateVisualSlopeHandle()
		{
			if (visualslopehandle != null)
			{
				visualslopehandle.UnloadResource();
				visualslopehandle.ReloadResource();
			}
		}

		#endregion

		#region ================== Presentation

		// This creates the projection
		internal void CreateProjection()
		{
			// Calculate aspect
			float screenheight = General.Map.Graphics.RenderTarget.ClientSize.Height * (General.Settings.DOOMStretchView ? General.Map.Data.InvertedVerticalViewStretch : 1.0f); //mxd
			float aspect = General.Map.Graphics.RenderTarget.ClientSize.Width / screenheight;
			
			// The DirectX PerspectiveFovRH matrix method calculates the scaling in X and Y as follows:
			// yscale = 1 / tan(fovY / 2)
			// xscale = yscale / aspect
			// The fov specified in the method is the FOV over Y, but we want the user to specify the FOV
			// over X, so calculate what it would be over Y first;
			float fov = (float)Angle2D.DegToRad(General.Settings.VisualFOV);
			float reversefov = 1.0f / (float)Math.Tan(fov / 2.0f);
			float reversefovy = reversefov * aspect;
			float fovy = (float)Math.Atan(1.0f / reversefovy);
			
			// Make the projection matrix and its inverse
			projection = Matrix.PerspectiveFov(fovy, aspect, PROJ_NEAR_PLANE, General.Settings.ViewDistance);
			inverseprojection = Matrix.Invert(projection);

			// We also need to re-create the 2D matrices, otherwise the corsshair will be distorted after the viewport is resized. See
			// https://github.com/jewalky/UltimateDoomBuilder/issues/321
			// and
			// https://github.com/jewalky/UltimateDoomBuilder/issues/777
			CreateMatrices2D();
			crosshairverts = null;
		}
		
		// This creates matrices for a camera view
		public void PositionAndLookAt(Vector3D pos, Vector3D lookat)
		{
			float viewdistance = General.Settings.ViewDistance;
			int viewfov = General.Settings.VisualFOV;
			if(hascachedview && pos == cachedviewposition && lookat == cachedviewtarget
				&& viewdistance == cachedviewdistance && viewfov == cachedviewfov) return;

			cachedviewposition = pos;
			cachedviewtarget = lookat;
			cachedviewdistance = viewdistance;
			cachedviewfov = viewfov;
			hascachedview = true;

			// Calculate delta vector
			cameraposition = pos;
            Vector3D delta = lookat - pos;
            cameravector = delta.GetNormal();
			double anglexy = delta.GetAngleXY();
			double anglez = delta.GetAngleZ();

			// Create frustum
			frustum.Update(pos, (float)anglexy, (float)anglez, PROJ_NEAR_PLANE,
				viewdistance, (float)Angle2D.DegToRad(viewfov));

            // Make the view matrix and its inverse
            view3d = Matrix.LookAt(RenderDevice.V3(pos), RenderDevice.V3(lookat), new Vector3f(0f, 0f, 1f));
			inverseview3d = Matrix.Invert(view3d);
			
			// Make the billboard matrix
			billboard = Matrix.RotationZ((float)(anglexy + Angle2D.PI));
		}
		
		
		// volte: Set the active colormap for classic lighting
		public void SetClassicLightingColorMap(Data.ColorMap colormap)
		{
			if (colormap == classicLightingColorMap) 
			{
				return;
			}
			classicLightingColorMap = colormap;
			if (classicLightingColorMap == null)
			{
				classicLightingColorMapTex = null;
			}
			else 
			{
				classicLightingColorMapTex = new Texture(graphics, classicLightingColorMap.CreateBitmap());
			}
		}
		
		// This creates 2D view matrix
		private void CreateMatrices2D()
		{
			windowsize = graphics.RenderTarget.ClientSize;
			Matrix scaling = Matrix.Scaling((1f / windowsize.Width) * 2f, (1f / windowsize.Height) * -2f, 1f);
			Matrix translate = Matrix.Translation(-(float)windowsize.Width * 0.5f, -(float)windowsize.Height * 0.5f, 0f);
			view2d = translate * scaling;
		}

		#endregion

		#region ================== Start / Finish

		// This starts rendering
		public bool Start()
		{
			renderframestart = System.Diagnostics.Stopwatch.GetTimestamp();

			// Keep first-use GPU uploads from monopolizing a Visual Mode frame, but
			// increase the allowance automatically while the renderer has spare time.
			// A pixel budget prevents a run of very large textures from defeating the
			// item limit.
			General.Map.Data.BeginVisualTextureUploadFrame(adaptiveuploadcount,
				(long)adaptiveuploadcount * 1024L * 1024L);

            if (General.Settings.ShowFPS && !fpsWatch.IsRunning)
                fpsWatch.Start();

            // Start drawing
            graphics.StartRendering(true, General.Colors.Background.ToColorValue());

			// Beginning renderstates
			graphics.SetCullMode(Cull.None);
			graphics.SetZEnable(false);
			graphics.SetAlphaBlendEnable(false);
			graphics.SetAlphaTestEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
			graphics.SetUniform(UniformName.fogsettings, new Vector4f(-1.0f));
			graphics.SetUniform(UniformName.fogcolor, General.Colors.Background.ToColorValue());
			graphics.SetUniform(UniformName.doomlightlevels, General.Map.Config.DoomLightLevels);
            graphics.SetUniform(UniformName.highlightcolor, new Color4()); //mxd
            TextureFilter texFilter = (!General.Settings.ClassicRendering && General.Settings.VisualBilinear) ? TextureFilter.Linear : TextureFilter.Nearest;
            MipmapFilter mipFilter = General.Settings.ClassicRendering ? MipmapFilter.None : MipmapFilter.Linear;
            float aniso = General.Settings.ClassicRendering ? 0 : General.Settings.FilterAnisotropy;
            graphics.SetSamplerFilter(texFilter, texFilter, mipFilter, aniso);

            // Texture addressing
            graphics.SetSamplerState(TextureAddress.Wrap);

            // Matrices
			world = Matrix.Identity;
            graphics.SetUniform(UniformName.projection, ref projection);
            graphics.SetUniform(UniformName.world, ref world);
            graphics.SetUniform(UniformName.view, ref view3d);

			// Highlight
			if(General.Settings.AnimateVisualSelection)
			{
				highlightglow = (float)Math.Sin(Clock.CurrentTime / 100.0f) * 0.1f + 0.4f;
				highlightglowinv = -highlightglow + 0.8f;
			}
			else
			{
				highlightglow = 0.4f;
				highlightglowinv = 0.3f;
			}
				
			// Determine shader pass to use
			if (fullbrightness)
			{
				shaderpass = ShaderName.world3d_fullbright;
			} 
			else if (General.Settings.ClassicRendering)
			{
				shaderpass = ShaderName.world3d_classic;
			} 
			else
			{
				shaderpass = ShaderName.world3d_main;
			}

			// Create crosshair vertices
			if(crosshairverts == null)
				CreateCrosshairVerts(new Size(General.Map.Data.Crosshair3D.Width, General.Map.Data.Crosshair3D.Height));

			//mxd. Crate vertex handle
			if(vertexhandle == null) vertexhandle = new VisualVertexHandle();

			// Create slope handle
			if (visualslopehandle == null) visualslopehandle = new VisualSlopeHandle();

			// Ready
			return true;
		}
		
		// This begins rendering world geometry
		public void StartGeometry()
		{
			// Collections are emptied after the preceding frame and retained here.
			// Clear again defensively in case a render pass was interrupted.
			RecycleGeometryGroups(solidgeo);
			RecycleGeometryGroups(maskedgeo);
			RecycleThingGroups(solidthings);
			RecycleThingGroups(maskedthings);
			RecycleThingGroups(maskedmodelthings);
			translucentgeo.Clear();
			skygeo.Clear();
			translucentthings.Clear();
			translucentmodelthings.Clear();
			lightthings.Clear();
			allthings.Clear();
		}

		public void ApplyCurrentView()
		{
			graphics.SetUniform(UniformName.view, ref view3d);
		}

		public void BeginSkyboxTarget()
		{
			Size size = graphics.RenderTarget.ClientSize;
			if(skyboxrendertarget == null || skyboxrendertarget.Width != size.Width || skyboxrendertarget.Height != size.Height)
			{
				if(skyboxrendertarget != null) skyboxrendertarget.Dispose();
				skyboxrendertarget = new Texture(size.Width, size.Height, TextureFormat.Rgba8);
				skyboxscreenverts = CreateFullscreenVerts(size);
			}

			graphics.StartRendering(true, General.Colors.Background.ToColorValue(), skyboxrendertarget, true);
			Apply3DPassState();
		}

		public void CompositeSkyboxTarget()
		{
			graphics.StartRendering(true, General.Colors.Background.ToColorValue());
			graphics.SetCullMode(Cull.None);
			graphics.SetZEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetAlphaBlendEnable(false);
			graphics.SetAlphaTestEnable(false);
			graphics.SetShader(ShaderName.display2d_normal);
			graphics.SetTexture(skyboxrendertarget);
			world = Matrix.Identity;
			graphics.SetUniform(UniformName.world, ref world);
			graphics.SetUniform(UniformName.projection, world * view2d);
			graphics.SetUniform(UniformName.rendersettings, new Vector4f(1f, 1f, 0f, 1f));
			graphics.SetSamplerFilter(TextureFilter.Nearest, TextureFilter.Nearest, MipmapFilter.None, 0f);
			graphics.SetSamplerState(TextureAddress.Clamp);
			graphics.Draw(PrimitiveType.TriangleStrip, 0, 2, skyboxscreenverts);
			Apply3DPassState();
		}

		private void Apply3DPassState()
		{
			graphics.SetCullMode(Cull.None);
			graphics.SetZEnable(false);
			graphics.SetZWriteEnable(true);
			graphics.SetAlphaBlendEnable(false);
			graphics.SetAlphaTestEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
			TextureFilter texfilter = (!General.Settings.ClassicRendering && General.Settings.VisualBilinear) ? TextureFilter.Linear : TextureFilter.Nearest;
			MipmapFilter mipfilter = General.Settings.ClassicRendering ? MipmapFilter.None : MipmapFilter.Linear;
			graphics.SetSamplerFilter(texfilter, texfilter, mipfilter, General.Settings.ClassicRendering ? 0 : General.Settings.FilterAnisotropy);
			graphics.SetSamplerState(TextureAddress.Wrap);
			world = Matrix.Identity;
			graphics.SetUniform(UniformName.projection, ref projection);
			graphics.SetUniform(UniformName.world, ref world);
			graphics.SetUniform(UniformName.view, ref view3d);
		}

		private static FlatVertex[] CreateFullscreenVerts(Size size)
		{
			FlatVertex[] verts = new FlatVertex[4];
			// Native render targets use the opposite vertical texture origin from the
			// back buffer. Flip V while compositing so the live skybox remains upright.
			verts[0].x = 0f; verts[0].y = 0f; verts[0].c = -1; verts[0].u = 0f; verts[0].v = 1f;
			verts[1].x = size.Width; verts[1].y = 0f; verts[1].c = -1; verts[1].u = 1f; verts[1].v = 1f;
			verts[2].x = 0f; verts[2].y = size.Height; verts[2].c = -1; verts[2].u = 0f; verts[2].v = 0f;
			verts[3].x = size.Width; verts[3].y = size.Height; verts[3].c = -1; verts[3].u = 1f; verts[3].v = 0f;
			return verts;
		}

		public void SetSkyboxRenderPass(bool enabled)
		{
			skyboxrenderpass = enabled;
		}

		private List<VisualGeometry> AcquireGeometryGroup()
		{
			return (geometrygrouppool.Count > 0 ? geometrygrouppool.Pop() : new List<VisualGeometry>(16));
		}

		private List<VisualThing> AcquireThingGroup()
		{
			return (thinggrouppool.Count > 0 ? thinggrouppool.Pop() : new List<VisualThing>(16));
		}

		private void RecycleGeometryGroups(Dictionary<ImageData, List<VisualGeometry>> groups)
		{
			foreach(List<VisualGeometry> group in groups.Values)
			{
				group.Clear();
				geometrygrouppool.Push(group);
			}
			groups.Clear();
		}

		private void RecycleThingGroups<TKey>(Dictionary<TKey, List<VisualThing>> groups)
		{
			foreach(List<VisualThing> group in groups.Values)
			{
				group.Clear();
				thinggrouppool.Push(group);
			}
			groups.Clear();
		}

		// Upload each sector buffer once. VisualMode submits geometry in sector and
		// vertex-buffer order, and grouping by texture preserves that relative order.
		// Sorting every texture group again on every rendered frame was redundant and
		// particularly costly at 165/240 FPS on maps with many material groups.
		private void PrepareGeometryGroups(Dictionary<ImageData, List<VisualGeometry>> groups)
		{
			foreach(List<VisualGeometry> group in groups.Values)
			{
				foreach(VisualGeometry geometry in group)
				{
					VisualSector sector = geometry.Sector;
					if(!IsRenderableSector(sector)) continue;
					if(preparedgeometrysectors.Add(sector) && sector.NeedsUpdateGeo)
						sector.Update(graphics);
				}
			}
		}

		// Undo/redo can invalidate map elements immediately while a cached visual
		// collection still holds the corresponding renderer object. Treat those
		// objects as unavailable for the current frame; the visual-mode refresh will
		// replace them before the following frame.
		private static bool IsRenderableSector(VisualSector visualsector)
		{
			if(visualsector == null || visualsector.IsDisposed) return false;
			Sector sector = visualsector.Sector;
			return sector != null && !sector.IsDisposed && sector.Map != null && sector.Fields != null;
		}

		private static bool IsRenderableThing(VisualThing visualthing)
		{
			return visualthing != null && !visualthing.IsDisposed && visualthing.Thing != null
				&& !visualthing.Thing.IsDisposed && visualthing.Thing.Map != null;
		}

		// This ends rendering world geometry
		public void FinishGeometry()
		{
			preparedgeometrysectors.Clear();
			PrepareGeometryGroups(solidgeo);
			PrepareGeometryGroups(maskedgeo);

			//mxd. Sort lights
			if(General.Settings.GZDrawLightsMode != LightRenderMode.NONE && !fullbrightness && lightthings.Count > 0)
				UpdateLights();

			// Initial renderstates
			graphics.SetCullMode(Cull.Clockwise);
			graphics.SetZEnable(true);
			graphics.SetZWriteEnable(true);
			graphics.SetAlphaBlendEnable(false);
			graphics.SetAlphaTestEnable(false);

            //mxd. SKY PASS
			if (skygeo.Count > 0)
			{
				world = Matrix.Identity;
                graphics.SetUniform(UniformName.world, ref world);
				if(skyboxbackground && !skyboxrenderpass)
					RenderSkyDepthMask(skygeo);
				else
					RenderSky(skygeo);

				graphics.SetAlphaBlendEnable(false);
				graphics.SetAlphaTestEnable(false);
				graphics.SetZWriteEnable(true);
			}

			// SOLID PASS
			world = Matrix.Identity;
            graphics.SetUniform(UniformName.world, ref world);
            RenderSinglePass(solidgeo, solidthings, lightthings);

			//mxd. Render models, without backface culling
			if(maskedmodelthings.Count > 0)
			{
				graphics.SetAlphaTestEnable(true);
				graphics.SetCullMode(Cull.None);
				RenderModels(false, lightthings);
                graphics.SetCullMode(Cull.Clockwise);
			}

			// MASK PASS
			if(maskedgeo.Count > 0 || maskedthings.Count > 0)
			{
				world = Matrix.Identity;
                graphics.SetUniform(UniformName.world, ref world);
                graphics.SetAlphaTestEnable(true);
				RenderSinglePass(maskedgeo, maskedthings, lightthings);
			}

			// ALPHA AND ADDITIVE PASS
			if(translucentgeo.Count > 0 || translucentthings.Count > 0)
			{
				world = Matrix.Identity;
                graphics.SetUniform(UniformName.world, ref world);
                graphics.SetAlphaBlendEnable(true);
				graphics.SetAlphaTestEnable(false);
				graphics.SetZWriteEnable(false);
				graphics.SetSourceBlend(Blend.SourceAlpha);
				RenderTranslucentPass(translucentgeo, translucentthings, lightthings);
			}

			//mxd. Render translucent models, with backface culling
			if(translucentmodelthings.Count > 0)
			{
				graphics.SetAlphaBlendEnable(true);
				graphics.SetAlphaTestEnable(false);
				graphics.SetZWriteEnable(false);
				graphics.SetSourceBlend(Blend.SourceAlpha);
                RenderModels(true, lightthings);
            }

			// SRB2 spawns these as runtime children of torches, candles, and flame
			// holders. Draw the editor previews separately so they remain tied to the
			// source VisualThing for selection and movement.
			if(coronathings.Count > 0) RenderCoronas();

            // THING CAGES
			if (renderthingcages && !skyboxrenderpass)
			{
				world = Matrix.Identity;
                graphics.SetUniform(UniformName.world, ref world);
                RenderThingCages();
			}

			//mxd. Visual vertices
			if(!skyboxrenderpass) RenderVertices();

			// Slope handles
			if (!skyboxrenderpass && General.Map.UDMF /* && General.Settings.ShowVisualSlopeHandles */)
				RenderSlopeHandles();

			//mxd. Event lines
			if (!skyboxrenderpass && General.Settings.GZShowEventLines) RenderArrows(eventlines);
			
			// Remove references and recycle the per-texture groups for the next frame.
			graphics.SetTexture(null);
			RecycleGeometryGroups(solidgeo);
			RecycleGeometryGroups(maskedgeo);
			RecycleThingGroups(solidthings);
			RecycleThingGroups(maskedthings);
			RecycleThingGroups(maskedmodelthings);
			translucentgeo.Clear();
			skygeo.Clear();
			translucentthings.Clear();
			coronathings.Clear();
			translucentmodelthings.Clear();
			lightthings.Clear();
			allthings.Clear();

			visualvertices = null;
			if(!skyboxrenderpass) General.Map.Data.EndVisualTextureUploadFrame();

			if (!skyboxrenderpass && General.Settings.ShowFPS)
			{
				fps++;
				if (fpsWatch.ElapsedMilliseconds > 1000)
				{
					int measuredfps = fps;
					fpsLabel.Text = string.Format("{0} FPS", measuredfps);
					fpssamplecount++;
					if(fpssamplecount == 12)
						General.WriteLogLine("Visual Mode frame rate sample: " + measuredfps + " FPS (render "
							+ lastaveragecpumilliseconds.ToString("0.0") + " ms, present "
							+ lastaveragepresentmilliseconds.ToString("0.0") + " ms)");
					fps = 0;
					fpsWatch.Restart();
				}
			}
        }

        // [ZZ] black renderer magic here.
        //      todo maybe implement proper frustum culling eventually?
        //      Frustum2D.IntersectCircle doesn't seem to work here.
        private bool CullLight(VisualThing t)
        {
			Vector3D lightcenter = t.CenterV3D;
			Vector3D lightToCamera = cameraposition - lightcenter;
            double angdiff = Vector3D.DotProduct(lightToCamera, cameravector);
            if (angdiff <= 0)
                return true; // light in front of the camera. it's not negative because I don't want to calculate things twice and need the vector to point at camera.
            // otherwise check light size: large lights might have center on the back, but radius in front of the camera.
			lightToCamera = lightToCamera.GetNormal();
            Vector3D lightToCameraWithRadius = (cameraposition - (lightcenter + lightToCamera * t.LightRadius)).GetNormal();
            double angdiffWithRadius = Vector3D.DotProduct(lightToCameraWithRadius, cameravector);
            if (angdiffWithRadius <= 0)
                return true; // light's radius extension is in front of the camera.
            return false;
        }

        //mxd
		private void UpdateLights()
		{
			// Discard lights outside the view before calculating distances and sorting.
			// The old order sorted every submitted light even though many were removed
			// immediately afterward.
			int visiblecount = 0;
			for(int i = 0; i < lightthings.Count; i++)
			{
				if(CullLight(lightthings[i])) continue;
				lightthings[visiblecount++] = lightthings[i];
			}
			if(visiblecount < lightthings.Count)
				lightthings.RemoveRange(visiblecount, lightthings.Count - visiblecount);

			// Calculate distance to camera
			foreach(VisualThing t in lightthings) t.CalculateCameraDistance(cameraposition);

			// Keep only the closest lights. We do not need a complete distance ordering
			// because the retained set is grouped by render style immediately below.
			// A bounded max-heap changes the expensive O(n log n) full sort into
			// O(n log k), where k is the user's visible-light limit.
			int maximumlights = Math.Max(0, General.Settings.GZMaxDynamicLights);
			SelectClosestLights(maximumlights);

			// Sort things by light render style
			lightthings.Sort(CompareLightRenderStyle);
			Array.Clear(lightOffsets, 0, lightOffsets.Length);

			foreach(VisualThing t in lightthings) 
			{
				//add light to apropriate array.
				switch(t.LightType.LightRenderStyle) 
				{
					case GZGeneral.LightRenderStyle.NORMAL:
					case GZGeneral.LightRenderStyle.VAVOOM: lightOffsets[0]++; break;
					case GZGeneral.LightRenderStyle.ADDITIVE: lightOffsets[2]++; break;
                    case GZGeneral.LightRenderStyle.SUBTRACTIVE: lightOffsets[3]++; break;
					case GZGeneral.LightRenderStyle.LIGHTMAP: // Static lights look the same as attenuated lights
					default: lightOffsets[1]++; break; // attenuated
				}
			}

			PrepareLightShaderData();
		}

		private static int CompareLightRenderStyle(VisualThing first, VisualThing second)
		{
			return first.LightType.LightRenderStyle.CompareTo(second.LightType.LightRenderStyle);
		}

		private static void SiftDownFarthestLight(List<VisualThing> lights, int root, int count)
		{
			while(true)
			{
				int child = root * 2 + 1;
				if(child >= count) return;
				if(child + 1 < count
					&& CompareSortDistance(lights[child].CameraDistance, lights[child + 1].CameraDistance) < 0)
					child++;
				if(CompareSortDistance(lights[root].CameraDistance, lights[child].CameraDistance) >= 0) return;

				VisualThing swap = lights[root];
				lights[root] = lights[child];
				lights[child] = swap;
				root = child;
			}
		}

		private void SelectClosestLights(int maximumlights)
		{
			if(maximumlights <= 0)
			{
				lightthings.Clear();
				return;
			}
			if(lightthings.Count <= maximumlights) return;

			for(int i = maximumlights / 2 - 1; i >= 0; i--)
				SiftDownFarthestLight(lightthings, i, maximumlights);

			for(int i = maximumlights; i < lightthings.Count; i++)
			{
				if(CompareSortDistance(lightthings[i].CameraDistance, lightthings[0].CameraDistance) >= 0)
					continue;

				VisualThing replacement = lightthings[i];
				lightthings[i] = lightthings[0];
				lightthings[0] = replacement;
				SiftDownFarthestLight(lightthings, 0, maximumlights);
			}

			lightthings.RemoveRange(maximumlights, lightthings.Count - maximumlights);
		}

		private void EnsurePreparedLightCapacity(int count)
		{
			if(preparedlightcolors.Length >= count) return;
			int capacity = preparedlightcolors.Length;
			while(capacity < count) capacity *= 2;
			Array.Resize(ref preparedlightcolors, capacity);
			Array.Resize(ref preparedlightpositionsandradii, capacity);
			Array.Resize(ref preparedlightorientations, capacity);
			Array.Resize(ref preparedlightspotradii, capacity);
			Array.Resize(ref preparedlightstrengthsandlinearities, capacity);
		}

		private void PrepareLightShaderData()
		{
			preparedlightcount = lightthings.Count;
			EnsurePreparedLightCapacity(preparedlightcount);
			for(int i = 0; i < preparedlightcount; i++)
			{
				VisualThing light = lightthings[i];
				preparedlightcolors[i] = light.LightColor.ToVector();
				preparedlightpositionsandradii[i] = new Vector4f(light.Center, light.LightRadius);
				if(light.LightType.LightType == GZGeneral.LightType.SPOT)
				{
					preparedlightorientations[i] = new Vector4f(light.VectorLookAt, 1f);
					preparedlightspotradii[i] = new Vector2f(CosDeg(light.LightSpotRadius1), CosDeg(light.LightSpotRadius2));
				}
				else
				{
					preparedlightorientations[i] = new Vector4f();
					preparedlightspotradii[i] = new Vector2f();
				}

				float diameter = light.LightRadius * 2;
				preparedlightstrengthsandlinearities[i] = new Vector2f(
					Math.Min(1500.0f, (diameter * diameter) / 10), light.LightLinearity);
			}
		}

		private int GatherIntersectingLights(Vector3D[] boundingbox)
		{
			int lightindex = 0;
			// StartGeometry clears lightthings before every submission. If this frame has
			// no lights, preparedlightcount still describes the preceding frame until
			// UpdateLights runs, so cap against the current list as well.
			int sourcecount = Math.Min(preparedlightcount, lightthings.Count);
			for(int sourceindex = 0; sourceindex < sourcecount; sourceindex++)
			{
				if(!BoundingBoxesIntersect(boundingbox, lightthings[sourceindex].BoundingBox)) continue;

				gatheredlightsources[lightindex] = sourceindex;
				lightindex++;
				if(lightindex >= MAX_DYNLIGHTS_PER_SURFACE) break;
			}
			return lightindex;
		}

		private void ResetGatheredLightSet()
		{
			// Force the first surface of every pass to establish a complete uniform
			// state. This also clears light data retained by the preceding pass.
			previousgatheredlightcount = -1;
		}

		private bool PrepareGatheredLightShaderData(int lightcount)
		{
			if(lightcount == previousgatheredlightcount)
			{
				bool identical = true;
				for(int i = 0; i < lightcount; i++)
				{
					if(gatheredlightsources[i] == previousgatheredlightsources[i]) continue;
					identical = false;
					break;
				}
				if(identical) return false;
			}

			int previouscount = (previousgatheredlightcount < 0 ? lightcolors.Length : previousgatheredlightcount);
			for(int lightindex = 0; lightindex < lightcount; lightindex++)
			{
				int sourceindex = gatheredlightsources[lightindex];
				lightcolors[lightindex] = preparedlightcolors[sourceindex];
				lightpositionsandradii[lightindex] = preparedlightpositionsandradii[sourceindex];
				lightorientations[lightindex] = preparedlightorientations[sourceindex];
				lightspotradii[lightindex] = preparedlightspotradii[sourceindex];
				lightstrengthsandlinearities[lightindex] = preparedlightstrengthsandlinearities[sourceindex];
				previousgatheredlightsources[lightindex] = sourceindex;
			}

			for(int i = lightcount; i < previouscount; i++)
				lightcolors[i].W = 0;

			previousgatheredlightcount = lightcount;
			return true;
		}

		//mxd.
		//I never particularly liked old ThingCages, so I wrote this instead.
		//It should render faster and it has fancy arrow! :)
		private void RenderThingCages() 
		{
			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.SourceAlpha);

			graphics.SetShader(ShaderName.world3d_constant_color);

			foreach(VisualThing t in allthings)
			{
				// Setup color
				Color4 thingcolor;
				if(t.Selected && showselection) 
				{
					thingcolor = General.Colors.Selection3D.ToColorValue();
				} 
				else
				{
					thingcolor = t.CageColor;
					if(t != highlighted) thingcolor.Alpha = 0.6f;
				}
				graphics.SetUniform(UniformName.vertexColor, thingcolor);

				//Render cage
				graphics.SetVertexBuffer(t.CageBuffer);
				graphics.Draw(PrimitiveType.LineList, 0, t.CageLength);
			}
        }

		//mxd
		private void RenderVertices() 
		{
			if(visualvertices == null) return;

			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.SourceAlpha);

            graphics.SetShader(ShaderName.world3d_constant_color);

			foreach(VisualVertex v in visualvertices) 
			{
				world = v.Position;
                graphics.SetUniform(UniformName.world, ref world);

                // Setup color
                Color4 color;
				if(v.Selected && showselection) 
				{
					color = General.Colors.Selection3D.ToColorValue();
				} 
				else 
				{
					color = v.HaveHeightOffset ? General.Colors.InfoLine.ToColorValue() : General.Colors.Vertices.ToColorValue();
					if(v != highlighted) color.Alpha = 0.6f;
				}
                graphics.SetUniform(UniformName.vertexColor, color);

				//Commence drawing!!11
				graphics.SetVertexBuffer(v.CeilingVertex ? vertexhandle.Upper : vertexhandle.Lower);
				graphics.Draw(PrimitiveType.LineList, 0, 8);
			}
        }

		private void RenderSlopeHandles()
		{
			if (visualslopehandles == null || !showselection) return;

			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);

			graphics.SetShader(ShaderName.world3d_slope_handle);

			foreach (VisualSlope handle in visualslopehandles)
			{
				PixelColor color = General.Colors.Vertices;

				if (handle.Pivot)
					color = General.Colors.Guideline;
				else if (handle.Selected)
					color = General.Colors.Selection3D;
				else if (handle == highlighted)
					color = General.Colors.Highlight3D;
				else if (handle.SmartPivot)
					color = General.Colors.Vertices;

				world = handle.Position;
				graphics.SetUniform(UniformName.world, ref world);
				graphics.SetUniform(UniformName.vertexColor, color.ToColorValue());

				if (handle.Type == VisualSlopeType.Line)
				{
					graphics.SetUniform(UniformName.slopeHandleLength, (float)handle.Length);
					graphics.SetVertexBuffer(visualslopehandle.LineGeometry);
					graphics.Draw(PrimitiveType.TriangleList, 0, 2);
				}
				else if (handle.Type == VisualSlopeType.Vertex)
				{
					graphics.SetUniform(UniformName.slopeHandleLength, 1.0f);
					graphics.SetVertexBuffer(visualslopehandle.VertexGeometry);
					graphics.Draw(PrimitiveType.TriangleList, 0, 1);
				}
			}
		}

		//mxd
		private void RenderArrows(ICollection<Line3D> lines) 
		{
			if(lines.Count == 0) return;

			// Event lines change only after map/effect updates. Keep their uploaded GPU
			// buffer between frames instead of allocating, uploading, and destroying it
			// at the display rate.
			if(eventlinesdirty || eventlinebuffer == null || eventlinebuffer.Disposed)
			{
				int pointscount = 0;
				foreach(Line3D line in lines) pointscount += (line.RenderArrowhead ? 6 : 2);
				if(pointscount < 2) return;

				WorldVertex[] verts = new WorldVertex[pointscount];
				const float scaler = 20f;
				pointscount = 0;

				foreach(Line3D line in lines)
				{
					int color = line.Color.ToInt();
					verts[pointscount].x = (float)line.Start.x;
					verts[pointscount].y = (float)line.Start.y;
					verts[pointscount].z = (float)line.Start.z;
					verts[pointscount].c = color;
					pointscount++;

					verts[pointscount].x = (float)line.End.x;
					verts[pointscount].y = (float)line.End.y;
					verts[pointscount].z = (float)line.End.z;
					verts[pointscount].c = color;
					pointscount++;

					if(line.RenderArrowhead)
					{
						double nz = line.GetDelta().GetNormal().z * scaler;
						double angle = line.GetAngle();
						Vector3D a1 = new Vector3D(line.End.x - scaler * Math.Sin(angle - 0.46f), line.End.y + scaler * Math.Cos(angle - 0.46f), line.End.z - nz);
						Vector3D a2 = new Vector3D(line.End.x - scaler * Math.Sin(angle + 0.46f), line.End.y + scaler * Math.Cos(angle + 0.46f), line.End.z - nz);

						verts[pointscount] = verts[pointscount - 1];
						verts[pointscount + 1].x = (float)a1.x;
						verts[pointscount + 1].y = (float)a1.y;
						verts[pointscount + 1].z = (float)a1.z;
						verts[pointscount + 1].c = color;

						verts[pointscount + 2] = verts[pointscount - 1];
						verts[pointscount + 3].x = (float)a2.x;
						verts[pointscount + 3].y = (float)a2.y;
						verts[pointscount + 3].z = (float)a2.z;
						verts[pointscount + 3].c = color;
						pointscount += 4;
					}
				}

				if(eventlinebuffer != null) eventlinebuffer.Dispose();
				eventlinebuffer = new VertexBuffer();
				graphics.SetBufferData(eventlinebuffer, verts);
				eventlineprimitivecount = pointscount / 2;
				eventlinesdirty = false;
			}
			
			//begin rendering
			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.SourceAlpha);

            graphics.SetShader(ShaderName.world3d_vertex_color);

			world = Matrix.Identity;
            graphics.SetUniform(UniformName.world, ref world);

            //render
			graphics.SetVertexBuffer(eventlinebuffer);
			graphics.Draw(PrimitiveType.LineList, 0, eventlineprimitivecount);
		}

		// This performs a single render pass
		private void RenderSinglePass(Dictionary<ImageData, List<VisualGeometry>> geopass, Dictionary<ImageData, List<VisualThing>> thingspass, List<VisualThing> lights)
		{
			ImageData curtexture;
			ShaderName currentshaderpass = shaderpass;
			ShaderName highshaderpass = (ShaderName)(shaderpass + 2);

            // Begin rendering with this shader
            graphics.SetShader(shaderpass);

            // Light data arrays
            Vector4f[] lightColor = lightcolors;
            Vector4f[] lightPosAndRadius = lightpositionsandradii;
            Vector4f[] lightOrientation = lightorientations;
            Vector2f[] light2Radius = lightspotradii;
            Vector2f[] lightStrengthAndLinearity = lightstrengthsandlinearities;

            graphics.SetUniform(UniformName.lightsEnabled, lights.Count > 0);
            graphics.SetUniform(UniformName.ignoreNormals, false);
			
            graphics.SetUniform(UniformName.useLightStrength, General.Map.Data.MapInfo.LightAttenuationMode == "InverseSquare");
			graphics.SetUniform(UniformName.desaturation, 0.0f);

			ResetGatheredLightSet();

            // volte: Set textures for classic lighting
            if (classicLightingColorMap != null)
            {
	            graphics.SetUniform(UniformName.colormapSize, new Vector2i(classicLightingColorMapTex.Width, classicLightingColorMapTex.Height));
	            graphics.SetTexture(classicLightingColorMapTex, 1);
	            graphics.SetSamplerFilter(TextureFilter.Nearest, TextureFilter.Nearest, MipmapFilter.None, 0, 1);
	            graphics.SetSamplerState(TextureAddress.Clamp, 1);
            }

            // Render the geometry collected
            foreach (KeyValuePair<ImageData, List<VisualGeometry>> group in geopass)
			{
				curtexture = group.Key;

        // Apply texture
        Texture texture = UseIndexedTexture ? curtexture.IndexedTexture : curtexture.Texture;
        graphics.SetTexture(texture);
        graphics.SetUniform(UniformName.drawPaletted, texture.UserData == ImageData.TEXTURE_INDEXED);

				// Go for all geometry that uses this texture
				VisualSector sector = null;
				
				List<VisualGeometry> geometrygroup = group.Value;
				for(int geometryindex = 0; geometryindex < geometrygroup.Count; geometryindex++)
				{
					VisualGeometry g = geometrygroup[geometryindex];
					bool sectorchanged = false;

                    int lightIndex = GatherIntersectingLights(g.BoundingBox);

                    bool havelights = (lightIndex > 0);

					if (PrepareGatheredLightShaderData(lightIndex))
                    {
                        graphics.SetUniform(UniformName.lightColor, lightColor);
                        if (havelights)
                        {
                            graphics.SetUniform(UniformName.lightPosAndRadius, lightPosAndRadius);
                            graphics.SetUniform(UniformName.lightOrientation, lightOrientation);
                            graphics.SetUniform(UniformName.light2Radius, light2Radius);
                            graphics.SetUniform(UniformName.lightStrengthAndLinearity, lightStrengthAndLinearity);
                        }
                    }
					// Changing sector?
					if(!object.ReferenceEquals(g.Sector, sector))
					{
						// Update the sector if needed
						if(g.Sector.NeedsUpdateGeo) g.Sector.Update(graphics);

						// Only do this sector when a vertexbuffer is created
						//mxd. No Map means that sector was deleted recently, I suppose
						if(g.Sector.GeometryBuffer != null && g.Sector.Sector.Map != null)
						{
							// Change current sector
							sector = g.Sector;
							sectorchanged = true;

							// Set stream source
							graphics.SetVertexBuffer(sector.GeometryBuffer);
						}
						else
						{
							sector = null;
						}
					}

                    if (sector != null)
					{
						// Determine the shader pass we want to use for this object
						ShaderName wantedshaderpass = (((g == highlighted) && showhighlight) || (g.Selected && showselection)) ? highshaderpass : shaderpass;

						//mxd. Render fog?
						if(General.Settings.GZDrawFog && !fullbrightness && !General.Settings.ClassicRendering && sector.Sector.FogMode != SectorFogMode.NONE)
							wantedshaderpass += 8;

						// Switch shader pass?
						bool shaderchanged = currentshaderpass != wantedshaderpass;
						if(shaderchanged)
						{
							graphics.SetShader(wantedshaderpass);
							currentshaderpass = wantedshaderpass;

							//mxd. Set variables for fog rendering?
							if(wantedshaderpass > ShaderName.world3d_p7)
							{
								graphics.SetUniform(UniformName.modelnormal, Matrix.Identity);
                            }
						}

						// These values are constant for all geometry in the same sector and
						// shader. Avoid two driver uniform calls for every wall triangle group.
						if(sectorchanged || shaderchanged)
						{
							graphics.SetUniform(UniformName.sectorLightLevel, sector.Sector.Brightness);
							graphics.SetUniform(UniformName.desaturation, (float)sector.Sector.Desaturation);
						}

						//mxd. Set variables for fog rendering?
						if(wantedshaderpass > ShaderName.world3d_p7)
						{
							graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, g.FogFactor));
							graphics.SetUniform(UniformName.sectorfogcolor, sector.Sector.FogColor);
						}
                        
						// Set the colors to use
						graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((g == highlighted) && showhighlight, (g.Selected && showselection)));

						// Skew
						graphics.SetUniform(UniformName.skew, g.Skew);

						// Adjacent pieces in the same sector, texture and render state occupy
						// adjacent ranges of one vertex buffer. Submit them together. Large SRB2
						// maps commonly turn thousands of driver calls into a few hundred here.
						int triangles = g.Triangles;
						if(!havelights)
						{
							bool highlightedstate = (g == highlighted) && showhighlight;
							bool selectedstate = g.Selected && showselection;
							int nextoffset = g.VertexOffset + g.Triangles * 3;
							while(geometryindex + 1 < geometrygroup.Count)
							{
								VisualGeometry next = geometrygroup[geometryindex + 1];
								if(!object.ReferenceEquals(next.Sector, g.Sector)
									|| next.VertexOffset != nextoffset
									|| next.Skew != g.Skew
									|| next.FogFactor != g.FogFactor
									|| (((next == highlighted) && showhighlight) != highlightedstate)
									|| ((next.Selected && showselection) != selectedstate)
									|| HasIntersectingLight(next, lights)) break;

								triangles += next.Triangles;
								nextoffset += next.Triangles * 3;
								geometryindex++;
							}
						}

						graphics.Draw(PrimitiveType.TriangleList, g.VertexOffset, triangles);
					}
				}
			}

			// Done with geometry, reset potentially lingering skew setting
			graphics.SetUniform(UniformName.skew, new Vector2f(0.0f, 0.0f));

			graphics.SetUniform(UniformName.lightsEnabled, false);

            // Get things for this pass
            if (thingspass.Count > 0)
			{
				// Texture addressing
				graphics.SetSamplerState(TextureAddress.Clamp);
				graphics.SetCullMode(Cull.None); //mxd. Disable backside culling, because otherwise sprites with positive ScaleY and negative ScaleX will be facing away from the camera...

				Color4 vertexcolor = new Color4(); //mxd

				// Render things collected
				foreach(KeyValuePair<ImageData, List<VisualThing>> group in thingspass)
				{
					if(group.Key is UnknownImage) continue;
					
					curtexture = group.Key;

          // Apply texture
          Texture texture = UseIndexedTexture ? curtexture.IndexedTexture : curtexture.Texture;
          graphics.SetTexture(texture);
          graphics.SetUniform(UniformName.drawPaletted, texture.UserData == ImageData.TEXTURE_INDEXED);

					// Render all things with this texture
					foreach(VisualThing t in group.Value)
					{
						//mxd. Check 3D distance. Check against MaxValue to save doing GetLenthSq if there's not DistanceCheck defined
						if (t.Info.DistanceCheckSq < double.MaxValue)
						{
							t.CalculateCameraDistance(cameraposition);

							if (t.CameraDistance > t.Info.DistanceCheckSq)
								continue;
						}

						// Only do this sector when a vertexbuffer is created
						if (t.GeometryBuffer != null) 
						{
							// Determine the shader pass we want to use for this object
							ShaderName wantedshaderpass = (((t == highlighted) && showhighlight) || (t.Selected && showselection)) ? highshaderpass : shaderpass;

							//mxd. If fog is enagled, switch to shader, which calculates it
							if(General.Settings.GZDrawFog && !fullbrightness && !General.Settings.ClassicRendering && t.Thing.Sector != null && t.Thing.Sector.FogMode != SectorFogMode.NONE)
								wantedshaderpass += 8;

							//mxd. Create the matrix for positioning 
							world = CreateThingPositionMatrix(t);

							//mxd. If current thing is light - set it's color to light color
							if(t.LightType != null && t.LightType.LightInternal && t.LightType.LightType != GZGeneral.LightType.SUN && !fullbrightness && !General.Settings.ClassicRendering) 
							{
								wantedshaderpass += 4; // Render using one of passes, which uses World3D.VertexColor
								vertexcolor = t.LightColor;
							}
							//mxd. Check if Thing is affected by dynamic lights and set color accordingly
							else if(General.Settings.GZDrawLightsMode != LightRenderMode.NONE && !fullbrightness && !General.Settings.ClassicRendering && lightthings.Count > 0)
							{
								Color4 litcolor = GetLitColorForThing(t);
								if(litcolor.ToArgb() != 0)
								{
									wantedshaderpass += 4; // Render using one of passes, which uses World3D.VertexColor
									vertexcolor = new Color4(t.VertexColor) + litcolor;
								}
							}
							else
							{
								vertexcolor = new Color4();
							}

							// Switch shader pass?
							if(currentshaderpass != wantedshaderpass) 
							{
								graphics.SetShader(wantedshaderpass);
								currentshaderpass = wantedshaderpass;
							}

							//mxd. Set variables for fog rendering?
							if(wantedshaderpass > ShaderName.world3d_p7)
							{
                                graphics.SetUniform(UniformName.modelnormal, Matrix.Identity);
                                graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, t.FogFactor));
							}

							// Set the colors to use
							if (t.Thing.Sector != null)
							{
								graphics.SetUniform(UniformName.sectorLightLevel, t.Thing.Sector.Brightness);
								graphics.SetUniform(UniformName.sectorfogcolor, t.Thing.Sector.FogColor);
							}
                            graphics.SetUniform(UniformName.vertexColor, vertexcolor);
                            graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((t == highlighted) && showhighlight, (t.Selected && showselection)));

                            // [ZZ] check if we want stencil
                            graphics.SetUniform(UniformName.stencilColor, t.StencilColor.ToColorValue());

                            // [ZZ] apply desaturation
                            if (t.Thing.Sector != null)
                                graphics.SetUniform(UniformName.desaturation, (float)t.Thing.Sector.Desaturation);
                            else graphics.SetUniform(UniformName.desaturation, 0.0f);

                            // Apply changes
                            graphics.SetUniform(UniformName.world, world);

                            // Apply buffer
                            graphics.SetVertexBuffer(t.GeometryBuffer);

							// Render!
							graphics.Draw(PrimitiveType.TriangleList, 0, t.Triangles);
                        }
					}

                    // [ZZ]
                    graphics.SetUniform(UniformName.stencilColor, new Color4(1f, 1f, 1f, 0f));
                }

                // Texture addressing
                graphics.SetSamplerState(TextureAddress.Wrap);
				graphics.SetCullMode(Cull.Clockwise); //mxd
			}
		}

		// Return a total ordering even when malformed geometry contains a NaN distance.
		// List<T>.Sort throws when a comparer gives inconsistent answers, so all visual
		// sorting goes through this helper instead of casting a floating-point delta.
		private static int CompareSortDistance(double first, double second)
		{
			if(double.IsNaN(first)) first = double.NegativeInfinity;
			if(double.IsNaN(second)) second = double.NegativeInfinity;
			return first.CompareTo(second);
		}

		private static double GetGeometrySortDistance(VisualGeometry geometry, Vector3D camera)
		{
			if(geometry.GeometryType == VisualGeometryType.FLOOR
				|| geometry.GeometryType == VisualGeometryType.CEILING)
				return Math.Abs(geometry.BoundingBox[0].z - camera.z);

			return (camera - geometry.BoundingBox[0]).GetLengthSq();
		}

		private static double GetThingSortDistance(VisualThing thing, Vector3D camera)
		{
			return (camera - thing.BoundingBox[0]).GetLengthSq();
		}

		//mxd
		private void RenderTranslucentPass(List<VisualGeometry> geopass, List<VisualThing> thingspass, List<VisualThing> lights)
		{
			ShaderName currentshaderpass = shaderpass;
			ShaderName highshaderpass = (ShaderName)(shaderpass + 2);

			// Calculate each distance once. A comparison sort can inspect the same item
			// many times, so doing the vector math inside the comparer multiplied this cost
			// on scenes containing many translucent surfaces.
			Vector3D sortcameraposition = cameraposition;
			for(int i = 0; i < geopass.Count; i++)
				geopass[i].SortDistance = GetGeometrySortDistance(geopass[i], sortcameraposition);
            geopass.Sort(delegate(VisualGeometry vg1, VisualGeometry vg2)
			{
                if (vg1 == vg2)
                    return 0;
				return CompareSortDistance(vg2.SortDistance, vg1.SortDistance);
			});

			ImageData curtexture;
			VisualSector sector = null;
			RenderPass currentpass = RenderPass.Solid;
			long curtexturename = 0;
			float fogfactor = -1;

			// Begin rendering with this shader
			graphics.SetShader(shaderpass);


            // Light data arrays
            Vector4f[] lightColor = lightcolors;
            Vector4f[] lightPosAndRadius = lightpositionsandradii;
            Vector4f[] lightOrientation = lightorientations;
            Vector2f[] light2Radius = lightspotradii;
            Vector2f[] lightStrengthAndLinearity = lightstrengthsandlinearities;

            graphics.SetUniform(UniformName.lightsEnabled, lights.Count > 0);
            graphics.SetUniform(UniformName.ignoreNormals, false);
			
            graphics.SetUniform(UniformName.useLightStrength, General.Map.Data.MapInfo.LightAttenuationMode == "InverseSquare");

			ResetGatheredLightSet();

            // Go for all geometry
            foreach (VisualGeometry g in geopass)
			{

                int lightIndex = GatherIntersectingLights(g.BoundingBox);

                bool havelights = (lightIndex > 0);

				if (PrepareGatheredLightShaderData(lightIndex))
                {
                    graphics.SetUniform(UniformName.lightColor, lightColor);
                    if (havelights)
                    {
                        graphics.SetUniform(UniformName.lightPosAndRadius, lightPosAndRadius);
                        graphics.SetUniform(UniformName.lightOrientation, lightOrientation);
                        graphics.SetUniform(UniformName.light2Radius, light2Radius);
                        graphics.SetUniform(UniformName.lightStrengthAndLinearity, lightStrengthAndLinearity);
                    }
                }
                // Change blend mode?
                if (g.RenderPass != currentpass)
				{
					switch(g.RenderPass)
					{
						case RenderPass.Additive:
							graphics.SetDestinationBlend(Blend.One);
							break;

						case RenderPass.Alpha:
							graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
							break;
					}

					currentpass = g.RenderPass;
				}

				// Change texture?
				if(g.Texture.LongName != curtexturename)
				{
					curtexture = g.Texture;

					// Apply texture
					Texture texture = UseIndexedTexture ? curtexture.IndexedTexture : curtexture.Texture;
					graphics.SetTexture(texture);
					graphics.SetUniform(UniformName.drawPaletted, texture.UserData == ImageData.TEXTURE_INDEXED);
					
					curtexturename = g.Texture.LongName;
				}

				// Changing sector?
				if(!object.ReferenceEquals(g.Sector, sector))
				{
					// Update the sector if needed
					if(g.Sector.NeedsUpdateGeo) g.Sector.Update(graphics);

					// Only do this sector when a vertexbuffer is created
					//mxd. No Map means that sector was deleted recently, I suppose
					if(g.Sector.GeometryBuffer != null && g.Sector.Sector.Map != null)
					{
						// Change current sector
						sector = g.Sector;

						// Set stream source
						graphics.SetVertexBuffer(sector.GeometryBuffer);
					}
					else
					{
						sector = null;
					}
				}

                if (sector != null)
                {
                    // Determine the shader pass we want to use for this object
                    ShaderName wantedshaderpass = (((g == highlighted) && showhighlight) || (g.Selected && showselection)) ? highshaderpass : shaderpass;

                    //mxd. Render fog?
                    if (General.Settings.GZDrawFog && !fullbrightness && !General.Settings.ClassicRendering && sector.Sector.FogMode != SectorFogMode.NONE)
                        wantedshaderpass += 8;

                    // Switch shader pass?
                    if (currentshaderpass != wantedshaderpass)
                    {
                        graphics.SetShader(wantedshaderpass);
                        currentshaderpass = wantedshaderpass;

                        //mxd. Set variables for fog rendering?
                        if (wantedshaderpass > ShaderName.world3d_p7)
                        {
                            graphics.SetUniform(UniformName.modelnormal, Matrix.Identity);
                        }
                    }

                    // Set variables for fog rendering?
                    if (wantedshaderpass > ShaderName.world3d_p7 && g.FogFactor != fogfactor)
                    {
                        graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, g.FogFactor));
                        fogfactor = g.FogFactor;
                    }

                    //
                    graphics.SetUniform(UniformName.desaturation, (float)sector.Sector.Desaturation);

					// Skew
					graphics.SetUniform(UniformName.skew, g.Skew);

					// Set the colors to use
					graphics.SetUniform(UniformName.sectorfogcolor, sector.Sector.FogColor);
                    graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((g == highlighted) && showhighlight, (g.Selected && showselection)));

                    // Render!
                    graphics.Draw(PrimitiveType.TriangleList, g.VertexOffset, g.Triangles);
                }
                else graphics.SetUniform(UniformName.desaturation, 0.0f);
            }

			// Done with geometry, reset potentially lingering skew setting
			graphics.SetUniform(UniformName.skew, new Vector2f(0.0f, 0.0f));

			graphics.SetUniform(UniformName.lightsEnabled, false);

            // Get things for this pass
            if (thingspass.Count > 0)
			{
				// Texture addressing
				graphics.SetSamplerState(TextureAddress.Clamp);
				graphics.SetCullMode(Cull.None); //mxd. Disable backside culling, because otherwise sprites with positive ScaleY and negative ScaleX will be facing away from the camera...

				// Sort things by a distance calculated once for this pass.
				for(int i = 0; i < thingspass.Count; i++)
					thingspass[i].SortDistance = GetThingSortDistance(thingspass[i], sortcameraposition);
				thingspass.Sort(delegate(VisualThing vt1, VisualThing vt2)
				{
					if(vt1 == vt2) return 0;
					return CompareSortDistance(vt2.SortDistance, vt1.SortDistance);
				});

				// Reset vars
				currentpass = RenderPass.Solid;
				curtexturename = 0;
				Color4 vertexcolor = new Color4();
				fogfactor = -1;

				// Render things collected
				foreach(VisualThing t in thingspass)
				{
					//mxd. Check 3D distance. Check against MaxValue to save doing GetLenthSq if there's not DistanceCheck defined
					if (t.Info.DistanceCheckSq < double.MaxValue)
					{
						t.CalculateCameraDistance(cameraposition);

						if (t.CameraDistance > t.Info.DistanceCheckSq)
							continue;
					}

					// AddThingGeometry already selected the rotation frame for this camera
					// before placing the thing in its texture group. Repeating the angle
					// calculation here doubled that work for every visible sprite and could
					// not change safely without also invalidating the chosen texture group.
					if(t.Texture is UnknownImage) continue;
					
					// Change blend mode?
					if(t.RenderPass != currentpass)
					{
						switch(t.RenderPass)
						{
							case RenderPass.Additive:
								graphics.SetDestinationBlend(Blend.One);
								break;

							case RenderPass.Alpha:
								graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
								break;
						}

						currentpass = t.RenderPass;
					}

					// Change texture?
					if(t.Texture.LongName != curtexturename)
					{
						curtexture = t.Texture;

						// Apply texture
						Texture texture = UseIndexedTexture ? curtexture.IndexedTexture : curtexture.Texture;
						graphics.SetTexture(texture);
						graphics.SetUniform(UniformName.drawPaletted, texture.UserData == ImageData.TEXTURE_INDEXED);
						
						curtexturename = t.Texture.LongName;
					}

					// Only do this sector when a vertexbuffer is created
					if(t.GeometryBuffer != null)
					{
						// Determine the shader pass we want to use for this object
						ShaderName wantedshaderpass = (((t == highlighted) && showhighlight) || (t.Selected && showselection)) ? highshaderpass : shaderpass;

						//mxd. if fog is enagled, switch to shader, which calculates it
						if(General.Settings.GZDrawFog && !fullbrightness && !General.Settings.ClassicRendering && t.Thing.Sector != null && t.Thing.Sector.FogMode != SectorFogMode.NONE)
							wantedshaderpass += 8;

						//mxd. Create the matrix for positioning 
						world = CreateThingPositionMatrix(t);

						//mxd. If current thing is light - set it's color to light color
						if(t.LightType != null && t.LightType.LightInternal && !fullbrightness && !General.Settings.ClassicRendering)
						{
							wantedshaderpass += 4; // Render using one of passes, which uses World3D.VertexColor
							vertexcolor = t.LightColor;
						}
						//mxd. Check if Thing is affected by dynamic lights and set color accordingly
						else if(General.Settings.GZDrawLightsMode != LightRenderMode.NONE && !fullbrightness && !General.Settings.ClassicRendering && lightthings.Count > 0)
						{
							Color4 litcolor = GetLitColorForThing(t);
							if(litcolor.ToArgb() != 0)
							{
								wantedshaderpass += 4; // Render using one of passes, which uses World3D.VertexColor
								vertexcolor = new Color4(t.VertexColor) + litcolor;
							}
						}

						else
						{
							vertexcolor = new Color4();
						}

						// Switch shader pass?
						if(currentshaderpass != wantedshaderpass)
						{
							graphics.SetShader(wantedshaderpass);
							currentshaderpass = wantedshaderpass;
						}

						//mxd. Set variables for fog rendering?
						if(wantedshaderpass > ShaderName.world3d_p7)
						{
                            graphics.SetUniform(UniformName.modelnormal, Matrix.Identity);
                            if (t.FogFactor != fogfactor)
							{
                                graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, t.FogFactor));
								fogfactor = t.FogFactor;
							}
						}

                        // Set the colors to use
                        graphics.SetUniform(UniformName.sectorfogcolor, t.Thing.Sector.FogColor);
                        graphics.SetUniform(UniformName.vertexColor, vertexcolor);
                        graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((t == highlighted) && showhighlight, (t.Selected && showselection)));

                        // [ZZ] check if we want stencil
                        graphics.SetUniform(UniformName.stencilColor, t.StencilColor.ToColorValue());

                        //
                        graphics.SetUniform(UniformName.desaturation, (float)t.Thing.Sector.Desaturation);

						// Apply changes
						graphics.SetUniform(UniformName.world, world);

                        // Apply buffer
                        graphics.SetVertexBuffer(t.GeometryBuffer);

						// Render!
						graphics.Draw(PrimitiveType.TriangleList, 0, t.Triangles);
					}
				}

                // [ZZ] check if we want stencil
                graphics.SetUniform(UniformName.stencilColor, new Color4(1f, 1f, 1f, 0f));

                // Texture addressing
                graphics.SetSamplerState(TextureAddress.Wrap);
				graphics.SetCullMode(Cull.Clockwise); //mxd
			}
		}

		//mxd
		private Matrix CreateThingPositionMatrix(VisualThing t)
		{
			// Use normal ThingRenderMode when model rendering is disabled for this thing
			ThingRenderMode rendermode = t.Thing.RenderMode;
			if((t.Thing.RenderMode == ThingRenderMode.MODEL || t.Thing.RenderMode == ThingRenderMode.VOXEL) &&
			   (General.Settings.GZDrawModelsMode == ModelRenderMode.NONE ||
			   (General.Settings.GZDrawModelsMode == ModelRenderMode.SELECTION && !t.Selected)))
			{
				rendermode = ThingRenderMode.NORMAL;
			}
			
			// Create the matrix for positioning
			switch(rendermode)
			{
				case ThingRenderMode.NORMAL:
					if(t.Info.XYBillboard) // Apply billboarding?
					{
						return Matrix.Translation(0f, 0f, -t.LocalCenterZ)
							* Matrix.RotationX((float)(Angle2D.PI - General.Map.VisualCamera.AngleZ))
							* Matrix.Translation(0f, 0f, t.LocalCenterZ)
							* billboard
							* t.Position;
					}
					// VisualThing.Position is a translation matrix. Multiplying the shared
					// billboard by that matrix only copies its translation into M41-M43,
					// but the generic operator crosses the managed/native boundary and does
					// a full 4x4 multiply for every visible sprite on every frame.
					Matrix spriteposition = billboard;
					Matrix thingposition = t.Position;
					spriteposition.M41 = thingposition.M41;
					spriteposition.M42 = thingposition.M42;
					spriteposition.M43 = thingposition.M43;
					return spriteposition;

				case ThingRenderMode.FLATSPRITE:
				case ThingRenderMode.WALLSPRITE:
				case ThingRenderMode.MODEL:
				case ThingRenderMode.VOXEL:
					return t.Position;

				default: throw new NotImplementedException("Unknown ThingRenderMode");
			}
		}

		private void RenderCoronas()
		{
			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetZWriteEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
			graphics.SetCullMode(Cull.None);
			graphics.SetSamplerState(TextureAddress.Clamp);
			graphics.SetShader(ShaderName.world3d_fullbright);
			graphics.SetUniform(UniformName.lightsEnabled, false);
			graphics.SetUniform(UniformName.desaturation, 0.0f);
			graphics.SetUniform(UniformName.skew, new Vector2f());
			graphics.SetUniform(UniformName.stencilColor, new Color4(1f, 1f, 1f, 0f));
			graphics.SetUniform(UniformName.highlightcolor, new Color4());

			long currenttexture = long.MinValue;
			for(int i = 0; i < coronathings.Count; i++)
			{
				VisualThing t = coronathings[i];
				if(t.CoronaBuffer == null || t.CoronaTexture == null || t.CoronaTexture is UnknownImage) continue;

				if(t.CoronaTexture.LongName != currenttexture)
				{
					Texture texture = UseIndexedTexture ? t.CoronaTexture.IndexedTexture : t.CoronaTexture.Texture;
					graphics.SetTexture(texture);
					graphics.SetUniform(UniformName.drawPaletted, texture.UserData == ImageData.TEXTURE_INDEXED);
					currenttexture = t.CoronaTexture.LongName;
				}

				// Corona geometry contains its SRB2 height offset. It otherwise uses the
				// normal camera-facing billboard and the source object's translation.
				Matrix coronaposition = billboard;
				Matrix thingposition = t.Position;
				coronaposition.M41 = thingposition.M41;
				coronaposition.M42 = thingposition.M42;
				coronaposition.M43 = thingposition.M43;
				graphics.SetUniform(UniformName.world, coronaposition);
				graphics.SetVertexBuffer(t.CoronaBuffer);
				graphics.Draw(PrimitiveType.TriangleList, 0, t.CoronaTriangles);
			}

			graphics.SetSamplerState(TextureAddress.Wrap);
			graphics.SetCullMode(Cull.Clockwise);
		}

        private float CosDeg(float angle)
        {
            return (float)Math.Cos(Angle2D.DegToRad(angle));
        }

        //mxd. Render models
        private void RenderModels(bool trans, List<VisualThing> lights) 
		{
			ShaderName shaderpass = (fullbrightness ? ShaderName.world3d_fullbright : ShaderName.world3d_main_vertexcolor);
			ShaderName currentshaderpass = shaderpass;
			ShaderName highshaderpass = (ShaderName)(shaderpass + 2);

            RenderPass currentpass = RenderPass.Solid;

            // Begin rendering with this shader
            graphics.SetShader(currentshaderpass);

            // Light data arrays
            Vector4f[] lightColor = lightcolors;
            Vector4f[] lightPosAndRadius = lightpositionsandradii;
            Vector4f[] lightOrientation = lightorientations;
            Vector2f[] light2Radius = lightspotradii;
            Vector2f[] lightStrengthAndLinearity = lightstrengthsandlinearities;

            graphics.SetUniform(UniformName.lightsEnabled, lights.Count > 0);
            graphics.SetUniform(UniformName.ignoreNormals, true);
			
            graphics.SetUniform(UniformName.useLightStrength, General.Map.Data.MapInfo.LightAttenuationMode == "InverseSquare");

			ResetGatheredLightSet();

            List<VisualThing> things;
            if (trans)
            {
				// Calculate once per model instead of once per sort comparison.
				Vector3D sortcameraposition = cameraposition;
				for(int i = 0; i < translucentmodelthings.Count; i++)
					translucentmodelthings[i].SortDistance = GetThingSortDistance(translucentmodelthings[i], sortcameraposition);
				translucentmodelthings.Sort((vt1, vt2) => CompareSortDistance(vt2.SortDistance, vt1.SortDistance));
                things = translucentmodelthings;
            }
            else
            {
				opaquemodelthings.Clear();
                foreach (KeyValuePair<ModelData, List<VisualThing>> group in maskedmodelthings)
                    foreach (VisualThing t in group.Value)
						opaquemodelthings.Add(t);
				things = opaquemodelthings;
            }
            
			foreach(VisualThing t in things) 
			{

                if (trans)
                {
                    // Change blend mode?
                    if (t.RenderPass != currentpass)
                    {
                        switch (t.RenderPass)
                        {
                            case RenderPass.Additive:
                                graphics.SetDestinationBlend(Blend.One);
                                break;

                            case RenderPass.Alpha:
                                graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
                                break;
                        }

                        currentpass = t.RenderPass;
                    }
                }

				// Check 3D distance. Check against MaxValue to save doing GetLenthSq if there's not DistanceCheck defined
				if (t.Info.DistanceCheckSq < double.MaxValue)
				{
					t.CalculateCameraDistance(cameraposition);

					if (t.CameraDistance > t.Info.DistanceCheckSq)
						continue;
				}
					
				Color4 vertexcolor = new Color4(t.VertexColor);

                // Check if model is affected by dynamic lights and set color accordingly
                graphics.SetUniform(UniformName.vertexColor, vertexcolor);

				// Determine the shader pass we want to use for this object
				ShaderName wantedshaderpass = ((((t == highlighted) && showhighlight) || (t.Selected && showselection)) ? highshaderpass : shaderpass);

				// If fog is enagled, switch to shader, which calculates it
				if (General.Settings.GZDrawFog && !fullbrightness && !General.Settings.ClassicRendering && t.Thing.Sector != null && t.Thing.Sector.FogMode != SectorFogMode.NONE)
					wantedshaderpass += 8;

				// Switch shader pass?
				if (currentshaderpass != wantedshaderpass)
				{
					graphics.SetShader(wantedshaderpass);
					currentshaderpass = wantedshaderpass;
				}

                // Set the colors to use
                graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((t == highlighted) && showhighlight, (t.Selected && showselection)));

				// Create the matrix for positioning / rotation. SRB2 object scale
				// (including a deterministic spawn-state A_SetScale) affects models as
				// well as sprites and collision dimensions.
				ModelData modeldata = General.Map.Data.ModeldefEntries[t.Thing.Type];
				double objectscale = t.Thing.GetEffectiveObjectScale();
				double sx = t.Thing.ScaleX * t.Thing.ActorScale.Width * objectscale;
				double sy = t.Thing.ScaleY * t.Thing.ActorScale.Height * objectscale;
                
				Matrix modelscale = Matrix.Scaling((float)sx, (float)sx, (float)sy);
				Matrix modelrotation = Matrix.RotationY((float)-t.Thing.RollRad) * Matrix.RotationX((float)-t.Thing.PitchRad) * Matrix.RotationZ((float)t.Thing.Angle);

				if(modeldata.UseRotationCenter)
					world = modeldata.Transform * modelscale * Matrix.Translation(-modeldata.RotationCenter) * modelrotation * Matrix.Translation(modeldata.RotationCenter) * t.Position;
				else
					world = modeldata.Transform * modelscale * modelrotation  * t.Position;
                graphics.SetUniform(UniformName.world, world);

                // Set variables for fog rendering
                if (wantedshaderpass > ShaderName.world3d_p7)
				{
                    // this is not right...
					graphics.SetUniform(UniformName.modelnormal, modeldata.TransformRotation * modelrotation);
                    if (t.Thing.Sector != null) graphics.SetUniform(UniformName.sectorfogcolor, t.Thing.Sector.FogColor);
                    graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, t.FogFactor));
				}

                if (t.Thing.Sector != null)
                    graphics.SetUniform(UniformName.desaturation, (float)t.Thing.Sector.Desaturation);
                else graphics.SetUniform(UniformName.desaturation, 0.0f);

                int lightIndex = GatherIntersectingLights(t.BoundingBox);

                bool havelights = (lightIndex > 0);

				if (PrepareGatheredLightShaderData(lightIndex))
                {
                    graphics.SetUniform(UniformName.lightColor, lightColor);
                    if (havelights)
                    {
                        graphics.SetUniform(UniformName.lightPosAndRadius, lightPosAndRadius);
                        graphics.SetUniform(UniformName.lightOrientation, lightOrientation);
                        graphics.SetUniform(UniformName.light2Radius, light2Radius);
                        graphics.SetUniform(UniformName.lightStrengthAndLinearity, lightStrengthAndLinearity);
                    }
                }
                GZModel model = General.Map.Data.ModeldefEntries[t.Thing.Type].Model;
                for (int j = 0; j < model.Meshes.Count; j++)
                {
                    graphics.SetTexture(model.Textures[j]);

                    // Render!
                    model.Meshes[j].Draw(graphics);
                }
			}

            graphics.SetUniform(UniformName.lightsEnabled, false);

        }

		//mxd
		private void RenderSky(IEnumerable<VisualGeometry> geo)
		{
			VisualSector sector = null;
			
			// Set render settings
			graphics.SetShader(ShaderName.world3d_skybox);
			graphics.SetTexture(General.Map.Data.SkyBox);
			graphics.SetUniform(UniformName.campos, new Vector4f((float)cameraposition.x, (float)cameraposition.y, (float)cameraposition.z, 0f));

			foreach(VisualGeometry g in geo)
			{
				// Changing sector?
				if(!object.ReferenceEquals(g.Sector, sector))
				{
					// Update the sector if needed
					if(g.Sector.NeedsUpdateGeo) g.Sector.Update(graphics);

					// Only do this sector when a vertexbuffer is created
					//mxd. No Map means that sector was deleted recently, I suppose
					if(g.Sector.GeometryBuffer != null && g.Sector.Sector.Map != null)
					{
						// Change current sector
						sector = g.Sector;

						// Set stream source
						graphics.SetVertexBuffer(sector.GeometryBuffer);
					}
					else
					{
						sector = null;
					}
				}

				if(sector != null)
				{
					graphics.SetUniform(UniformName.highlightcolor, CalculateHighlightColor((g == highlighted) && showhighlight, (g.Selected && showselection)));
					graphics.Draw(PrimitiveType.TriangleList, g.VertexOffset, g.Triangles);
				}
			}
		}

        // [ZZ] this is copied from GZDoom
        private float Smoothstep(float edge0, float edge1, float x)
        {
            double t = Math.Min(Math.Max((x - edge0) / (edge1 - edge0), 0.0), 1.0);
            return (float)(t * t * (3.0 - 2.0 * t));
        }

		private float clamp(float v, float mn, float mx)
		{
			return Math.Min(Math.Max(v, mn), mx);
		}

		private float mix(float a, float b, float v)
		{
			return a * (1 - v) + b * v;
		}

		float inverseSquareDistanceAttenuation(float dist, float radius, float strength, float linearity)
		{
			float a = dist / radius;
			float b = clamp(1.0f - a * a * a * a, 0.0f, 1.0f);
			return mix((b * b) / (dist * dist + 1.0f) * strength, clamp((radius - dist) / radius, 0.0f, 1.0f), linearity);
		}

		//mxd. This gets color from dynamic lights based on distance to thing. 
		//thing position must be in absolute cordinates 
		//(thing.Position.Z value is relative to floor of the sector the thing is in)
		private Color4 GetLitColorForThing(VisualThing t) 
		{
			Color4 litColor = new Color4();
			DynamicLightData owndata;
			bool dontlightself = General.Map.Data.GldefsEntries.TryGetValue(t.Thing.Type, out owndata)
				&& owndata.DontLightSelf;
			Vector3f thingcenter = t.Center;
			bool inversesquare = General.Map.Data.MapInfo.LightAttenuationMode == "InverseSquare";
			foreach(VisualThing lt in lightthings)
			{
				// Don't light self
				if(dontlightself && t.Thing.Index == lt.Thing.Index)
					continue;

				Vector3f lightcenter = lt.Center;
				float lightradius = lt.LightRadius;
				float distSquared = Vector3f.DistanceSquared(lightcenter, thingcenter);
                float radiusSquared = lightradius * lightradius;
				if(distSquared < radiusSquared) 
				{
                    int sign = (lt.LightType.LightRenderStyle == GZGeneral.LightRenderStyle.SUBTRACTIVE ? -1 : 1);
					Vector3f L = thingcenter - lightcenter;
                    float dist = L.Length();
					float attn;
					
					if(inversesquare)
					{
						float diameter = lightradius * 2;
						attn = inverseSquareDistanceAttenuation(Math.Max(dist, (float)Math.Sqrt(lightradius) * 2), diameter, Math.Min(1500.0f, (diameter * diameter) / 10), lt.LightLinearity);
					}
					else
					{
						attn = 1 - dist / lightradius;
					}

                    float scaler = attn * lt.LightColor.Alpha;

                    if (lt.LightType.LightType == GZGeneral.LightType.SPOT)
                    {
                        Vector3f lookAt = lt.VectorLookAt;
                        L.Normalize();
                        float cosDir = Vector3f.Dot(-L, lookAt);
                        scaler *= (float)Smoothstep(CosDeg(lt.LightSpotRadius2), CosDeg(lt.LightSpotRadius1), cosDir);
                    }

                    if (scaler > 0)
                    {
                        litColor.Red += lt.LightColor.Red * scaler * sign;
                        litColor.Green += lt.LightColor.Green * scaler * sign;
                        litColor.Blue += lt.LightColor.Blue * scaler * sign;
                    }
				}
			}

			return litColor;
		}

		// This calculates the highlight/selection color
		private Color4 CalculateHighlightColor(bool ishighlighted, bool isselected)
		{
			if(!ishighlighted && !isselected) return new Color4(); //mxd
			Color4 highlightcolor = isselected ? General.Colors.Selection.ToColorValue() : General.Colors.Highlight.ToColorValue();
			highlightcolor.Alpha = ishighlighted ? highlightglowinv : highlightglow;
			return highlightcolor;
		}
		
		// This finishes rendering
		public void Finish()
		{
			General.Plugins.OnPresentDisplayBegin();

			// Done
			graphics.FinishRendering();
			double cpuframemilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - renderframestart)
				* 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			long presentstart = System.Diagnostics.Stopwatch.GetTimestamp();
			graphics.Present();
			double presentmilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - presentstart)
				* 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			highlighted = null;

			// Presentation may wait for VSync, so tune from CPU/render work measured
			// before Present. This fills newly visible surfaces faster on light frames
			// and backs off immediately when uploads threaten the selected frame cap.
			int fpslimit = General.Settings.VisualModeFPSLimit;
			double targetmilliseconds = 1000.0 / (fpslimit > 0 ? fpslimit : 144.0);
			if(cpuframemilliseconds < targetmilliseconds * 0.55)
				adaptiveuploadcount = Math.Min(32, adaptiveuploadcount + 4);
			else if(cpuframemilliseconds > targetmilliseconds * 0.85)
				adaptiveuploadcount = Math.Max(2, adaptiveuploadcount / 2);

			// Some windowed OpenGL drivers acknowledge swap interval 1 but present at
			// about 60 Hz even on a high-refresh display. Only bypass that wait when a
			// two-second timing window proves Present is the bottleneck and the actual
			// renderer has enough headroom for the selected 100+ FPS cap.
			if(General.Settings.VisualModeVSync && fpslimit >= 100 && !highfpsvsyncfallbackattempted)
			{
				frametimingcputotal += cpuframemilliseconds;
				frametimingpresenttotal += presentmilliseconds;
				frametimingcount++;
				double timingwindowmilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - frametimingwindowstart)
					* 1000.0 / System.Diagnostics.Stopwatch.Frequency;
				if(timingwindowmilliseconds >= 2000.0 && frametimingcount > 0)
				{
						double averagecpu = frametimingcputotal / frametimingcount;
						double averagepresent = frametimingpresenttotal / frametimingcount;
					lastaveragecpumilliseconds = averagecpu;
					lastaveragepresentmilliseconds = averagepresent;
					if(averagepresent > targetmilliseconds * 1.35
						&& averagecpu < targetmilliseconds * 1.25)
					{
						highfpsvsyncfallbackattempted = true;
						if(graphics.UseSoftwareFramePacingForHighFrameRate())
							General.WriteLogLine("Visual Mode bypassed a slow OpenGL VSync wait (render "
								+ averagecpu.ToString("0.0") + " ms, present "
								+ averagepresent.ToString("0.0") + " ms) and retained the "
								+ fpslimit + " FPS software cap");
					}

					frametimingwindowstart = System.Diagnostics.Stopwatch.GetTimestamp();
					frametimingcputotal = 0.0;
					frametimingpresenttotal = 0.0;
					frametimingcount = 0;
				}
			}
			else if(!General.Settings.VisualModeVSync || fpslimit < 100)
			{
				highfpsvsyncfallbackattempted = false;
				frametimingwindowstart = System.Diagnostics.Stopwatch.GetTimestamp();
				frametimingcputotal = 0.0;
				frametimingpresenttotal = 0.0;
				frametimingcount = 0;
			}
		}

		// A live SRB2 skybox is rendered before the normal map. F_SKY1 surfaces in
		// the normal map must still occlude geometry behind them without replacing
		// the already-rendered skybox colour, so draw those planes depth-only by
		// blending fully transparent constant colour while depth writes remain on.
		private void RenderSkyDepthMask(IEnumerable<VisualGeometry> geo)
		{
			VisualSector sector = null;
			graphics.SetShader(ShaderName.world3d_constant_color);
			graphics.SetTexture(null);
			graphics.SetUniform(UniformName.vertexColor, new Color4(0f, 0f, 0f, 0f));
			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
			graphics.SetZWriteEnable(true);

			foreach(VisualGeometry g in geo)
			{
				if(!object.ReferenceEquals(g.Sector, sector))
				{
					if(g.Sector.NeedsUpdateGeo) g.Sector.Update(graphics);
					if(g.Sector.GeometryBuffer != null && g.Sector.Sector.Map != null)
					{
						sector = g.Sector;
						graphics.SetVertexBuffer(sector.GeometryBuffer);
					}
					else
					{
						sector = null;
					}
				}

				if(sector != null)
					graphics.Draw(PrimitiveType.TriangleList, g.VertexOffset, g.Triangles);
			}

			// The depth-only sky mask preserves the composited live skybox, but it also
			// used to discard the normal F_SKY1 hover/selection tint. Draw only that
			// tint as a translucent overlay after the mask so the editable main-map
			// surface remains visible without tinting the skybox scene itself.
			graphics.SetZWriteEnable(false);
			sector = null;
			foreach(VisualGeometry g in geo)
			{
				bool ishighlighted = (g == highlighted) && showhighlight;
				bool isselected = g.Selected && showselection;
				if(!ishighlighted && !isselected) continue;

				if(!object.ReferenceEquals(g.Sector, sector))
				{
					if(g.Sector.NeedsUpdateGeo) g.Sector.Update(graphics);
					if(g.Sector.GeometryBuffer != null && g.Sector.Sector.Map != null)
					{
						sector = g.Sector;
						graphics.SetVertexBuffer(sector.GeometryBuffer);
					}
					else
					{
						sector = null;
					}
				}

				if(sector != null)
				{
					graphics.SetUniform(UniformName.vertexColor, CalculateHighlightColor(ishighlighted, isselected));
					graphics.Draw(PrimitiveType.TriangleList, g.VertexOffset, g.Triangles);
				}
			}
		}
		
		#endregion
		
		#region ================== Rendering
		
		// This sets the highlighted object for the rendering
		public void SetHighlightedObject(IVisualPickable obj)
		{
			highlighted = obj;
		}
		
		// This collects a visual sector's geometry for rendering
		public void AddSectorGeometry(VisualGeometry g)
		{
			// Must have a texture and vertices
			if(g != null && IsRenderableSector(g.Sector) && g.Texture != null && g.Triangles > 0)
			{
				if(g.RenderAsSky && General.Settings.GZDrawSky)
				{
					skygeo.Add(g);
				}
				else
				{
					switch(g.RenderPass)
					{
						case RenderPass.Solid:
							List<VisualGeometry> solidgroup;
							if(!solidgeo.TryGetValue(g.Texture, out solidgroup))
							{
								solidgroup = AcquireGeometryGroup();
								solidgeo.Add(g.Texture, solidgroup);
							}
							solidgroup.Add(g);
							break;

						case RenderPass.Mask:
							List<VisualGeometry> maskgroup;
							if(!maskedgeo.TryGetValue(g.Texture, out maskgroup))
							{
								maskgroup = AcquireGeometryGroup();
								maskedgeo.Add(g.Texture, maskgroup);
							}
							maskgroup.Add(g);
							break;

						case RenderPass.Additive:
						case RenderPass.Alpha:
							translucentgeo.Add(g);
							break;

						default:
							throw new NotImplementedException("Geometry rendering of " + g.RenderPass + " render pass is not implemented!");
					}
				}
			}
		}

		// This collects a visual sector's geometry for rendering
		public void AddThingGeometry(VisualThing t)
		{
			if(!IsRenderableThing(t)) return;

			// The thing might have changed, especially when doing realtime editing from the edit thing dialog, so update it if necessary
			t.Update();
			if(t.HasCoronaPreview) coronathings.Add(t);

			//mxd. Gather lights
			if (General.Settings.GZDrawLightsMode != LightRenderMode.NONE && !fullbrightness && t.LightType != null)
			{
				t.UpdateLightRadius();
                if (t.LightRadius > 0)
				{
                    if (t.LightType != null && t.LightType.LightAnimated)
                        t.UpdateBoundingBox();
					lightthings.Add(t);
				}
			}

			//mxd. Gather models
			if((t.Thing.RenderMode == ThingRenderMode.MODEL || t.Thing.RenderMode == ThingRenderMode.VOXEL) && 
				(General.Settings.GZDrawModelsMode == ModelRenderMode.ALL ||
				 General.Settings.GZDrawModelsMode == ModelRenderMode.ACTIVE_THINGS_FILTER ||
				(General.Settings.GZDrawModelsMode == ModelRenderMode.SELECTION && t.Selected))) 
			{
                if (t.RenderPass == RenderPass.Mask ||
                    t.RenderPass == RenderPass.Solid ||
                    (t.RenderPass == RenderPass.Alpha && (t.VertexColor & 0xFF000000) == 0xFF000000))
				{
					ModelData mde = General.Map.Data.ModeldefEntries[t.Thing.Type];
					List<VisualThing> modelgroup;
					if(!maskedmodelthings.TryGetValue(mde, out modelgroup))
					{
						modelgroup = AcquireThingGroup();
						maskedmodelthings.Add(mde, modelgroup);
					}
					modelgroup.Add(t);
                }
                else if (t.RenderPass == RenderPass.Alpha || t.RenderPass == RenderPass.Additive)
                {
                    translucentmodelthings.Add(t);
                }
                else
                {
                    throw new NotImplementedException("Thing model rendering of " + t.RenderPass + " render pass is not implemented!");
                }
			}
			// Gather regular things
			else 
			{
				//mxd. Set correct texture, geobuffer and triangles count
				t.UpdateSpriteFrame();

				//Must have a texture!
				if(t.Texture != null)
				{
					//mxd
					switch(t.RenderPass)
					{
						case RenderPass.Solid:
							List<VisualThing> solidthinggroup;
							if(!solidthings.TryGetValue(t.Texture, out solidthinggroup))
							{
								solidthinggroup = AcquireThingGroup();
								solidthings.Add(t.Texture, solidthinggroup);
							}
							solidthinggroup.Add(t);
							break;

						case RenderPass.Mask:
							List<VisualThing> maskedthinggroup;
							if(!maskedthings.TryGetValue(t.Texture, out maskedthinggroup))
							{
								maskedthinggroup = AcquireThingGroup();
								maskedthings.Add(t.Texture, maskedthinggroup);
							}
							maskedthinggroup.Add(t);
							break;

						case RenderPass.Additive:
						case RenderPass.Alpha:
							translucentthings.Add(t);
							break;

						default:
							throw new NotImplementedException("Thing rendering of " + t.RenderPass + " render pass is not implemented!");
					}
				}
			}

			//mxd. Add to the plain list
			allthings.Add(t);
		}

		//mxd
		public void SetVisualVertices(List<VisualVertex> verts) { visualvertices = verts; }

		public void SetVisualSlopeHandles(ICollection<VisualSlope> handles) { visualslopehandles = handles; }

		//mxd
		public void SetEventLines(List<Line3D> lines)
		{
			eventlines = lines ?? new List<Line3D>();
			eventlinesdirty = true;
		}

		//mxd
		private static bool BoundingBoxesIntersect(Vector3D[] bbox1, Vector3D[] bbox2)
		{
			// This runs for every visible surface/light pair. Work directly on the
			// stored center/minimum points instead of constructing three temporary
			// vectors for each test.
			Vector3D center1 = bbox1[0];
			Vector3D center2 = bbox2[0];
			Vector3D minimum1 = bbox1[1];
			Vector3D minimum2 = bbox2[1];

			return (center1.x - minimum1.x) + (center2.x - minimum2.x) >= Math.Abs(center1.x - center2.x)
				&& (center1.y - minimum1.y) + (center2.y - minimum2.y) >= Math.Abs(center1.y - center2.y)
				&& (center1.z - minimum1.z) + (center2.z - minimum2.z) >= Math.Abs(center1.z - center2.z);
		}

		private static bool HasIntersectingLight(VisualGeometry geometry, List<VisualThing> lights)
		{
			for(int i = 0; i < lights.Count; i++)
				if(BoundingBoxesIntersect(geometry.BoundingBox, lights[i].BoundingBox)) return true;
			return false;
		}

		// This renders the crosshair
		public void RenderCrosshair()
		{
			//mxd
			world = Matrix.Identity;
            graphics.SetUniform(UniformName.world, world);

            // Set renderstates
            graphics.SetCullMode(Cull.None);
			graphics.SetZEnable(false);
			graphics.SetAlphaBlendEnable(true);
			graphics.SetAlphaTestEnable(false);
			graphics.SetSourceBlend(Blend.SourceAlpha);
			graphics.SetDestinationBlend(Blend.InverseSourceAlpha);
            graphics.SetShader(ShaderName.display2d_normal);
            graphics.SetUniform(UniformName.projection, world * view2d);
            graphics.SetUniform(UniformName.rendersettings, new Vector4f(1.0f, 1.0f, 0.0f, 1.0f));
            graphics.SetSamplerFilter(General.Settings.VisualBilinear ? TextureFilter.Linear : TextureFilter.Nearest);

            // Texture
            if (crosshairbusy)
			{
				graphics.SetTexture(General.Map.Data.CrosshairBusy3D.Texture);
			}
			else
			{
				graphics.SetTexture(General.Map.Data.Crosshair3D.Texture);
			}
			
			// Draw
            graphics.Draw(PrimitiveType.TriangleStrip, 0, 2, crosshairverts);

			if (General.Settings.ShowFPS)
				General.Map.Renderer2D.RenderText(fpsLabel);
        }

        // This switches fog on and off
        public void SetFogMode(bool usefog)
		{
            if (usefog)
            {
                graphics.SetUniform(UniformName.fogsettings, new Vector4f(General.Settings.ViewDistance * FOG_RANGE, General.Settings.ViewDistance, 0.0f, 0.0f));
            }
            else
            {
                graphics.SetUniform(UniformName.fogsettings, new Vector4f(-1.0f));
            }
		}

		// This siwtches crosshair busy icon on and off
		public void SetCrosshairBusy(bool busy)
		{
			crosshairbusy = busy;
		}
		
		#endregion
	}
}
