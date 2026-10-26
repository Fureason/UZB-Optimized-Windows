
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
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Geometry;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Text;
using CodeImp.DoomBuilder.Rendering.Shaders;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
    public class RenderDeviceException : Exception
    {
        public RenderDeviceException(string message) : base(message) { }
    }

    public class RenderDevice : IDisposable
    {
		// Uniforms are updated many thousands of times while rendering a large map.
		// Allocating a new managed array for every scalar/vector update creates enough
		// short-lived garbage to cause visible collection hitches in Visual Mode. The
		// native call copies the values synchronously, so one reusable buffer per
		// RenderDevice is sufficient (the device and GL context are UI-thread owned).
		private readonly float[] uniformfloatbuffer = new float[4];
		private readonly int[] uniformintbuffer = new int[4];
		private float[] uniformarraybuffer = new float[0];
		private const int uniformcount = (int)UniformName.useLightStrength + 1;
		private readonly float[] uniformfloatcache = new float[uniformcount * 4];
		private readonly byte[] uniformfloatcachecomponents = new byte[uniformcount];
		private readonly int[] uniformintcache = new int[uniformcount * 4];
		private readonly byte[] uniformintcachecomponents = new byte[uniformcount];
		private readonly Matrix[] uniformmatrixcache = new Matrix[uniformcount];
		private readonly bool[] uniformmatrixcachevalid = new bool[uniformcount];
		private ShaderName? managedshader;

		// The native renderer already ignores duplicate render-state changes, but every
		// call still crosses the managed/native boundary. Visual Mode repeats these
		// setters for many surfaces every frame, so remember the last requested state
		// here as well. Resource bindings are cached by managed object identity rather
		// than native handle; disposed objects are never treated as a cache hit, so a
		// later resource reusing the same native address is still bound correctly.
		private const int managedtextureunits = 10;
		private VertexBuffer managedvertexbuffer;
		private IndexBuffer managedindexbuffer;
		private bool managedvertexbuffervalid;
		private bool managedindexbuffervalid;
		private readonly BaseTexture[] managedtextures = new BaseTexture[managedtextureunits];
		private readonly bool[] managedtexturevalid = new bool[managedtextureunits];
		private bool? managedalphablendenable;
		private bool? managedalphatestenable;
		private Cull? managedcullmode;
		private BlendOperation? managedblendoperation;
		private Blend? managedsourceblend;
		private Blend? manageddestinationblend;
		private FillMode? managedfillmode;
		private bool? managedmultisampleantialias;
		private bool? managedzenable;
		private bool? managedzwriteenable;
		private readonly TextureAddress?[] managedsampleraddress = new TextureAddress?[managedtextureunits];
		private readonly TextureFilter?[] managedsamplerminfilter = new TextureFilter?[managedtextureunits];
		private readonly TextureFilter?[] managedsamplermagfilter = new TextureFilter?[managedtextureunits];
		private readonly MipmapFilter?[] managedsamplermipfilter = new MipmapFilter?[managedtextureunits];
		private readonly float[] managedsampleranisotropy = new float[managedtextureunits];
		private readonly bool[] managedsamplerfiltervalid = new bool[managedtextureunits];

		public RenderDevice(RenderTargetControl rendertarget)
		{
            RenderTarget = rendertarget;

            CreateDevice();

            DeclareUniform(UniformName.rendersettings, "rendersettings", UniformType.Vec4f);
            DeclareUniform(UniformName.projection, "projection", UniformType.Mat4);
            DeclareUniform(UniformName.desaturation, "desaturation", UniformType.Float);
            DeclareUniform(UniformName.highlightcolor, "highlightcolor", UniformType.Vec4f);
            DeclareUniform(UniformName.view, "view", UniformType.Mat4);
            DeclareUniform(UniformName.world, "world", UniformType.Mat4);
            DeclareUniform(UniformName.modelnormal, "modelnormal", UniformType.Mat4);
            DeclareUniform(UniformName.FillColor, "fillColor", UniformType.Vec4f);
            DeclareUniform(UniformName.vertexColor, "vertexColor", UniformType.Vec4f);
            DeclareUniform(UniformName.stencilColor, "stencilColor", UniformType.Vec4f);
            DeclareUniform(UniformName.lightPosAndRadius, "lightPosAndRadius", UniformType.Vec4fArray);
            DeclareUniform(UniformName.lightOrientation, "lightOrientation", UniformType.Vec4fArray);
            DeclareUniform(UniformName.light2Radius, "light2Radius", UniformType.Vec2fArray);
            DeclareUniform(UniformName.lightColor, "lightColor", UniformType.Vec4fArray);
            DeclareUniform(UniformName.ignoreNormals, "ignoreNormals", UniformType.Float);
            DeclareUniform(UniformName.spotLight, "spotLight", UniformType.Float);
            DeclareUniform(UniformName.campos, "campos", UniformType.Vec4f);
            DeclareUniform(UniformName.fogsettings, "fogsettings", UniformType.Vec4f);
            DeclareUniform(UniformName.fogcolor, "fogcolor", UniformType.Vec4f);
            DeclareUniform(UniformName.sectorfogcolor, "sectorfogcolor", UniformType.Vec4f);
            DeclareUniform(UniformName.lightsEnabled, "lightsEnabled", UniformType.Float);
			DeclareUniform(UniformName.slopeHandleLength, "slopeHandleLength", UniformType.Float);

			// vkdoom lights
            DeclareUniform(UniformName.lightStrengthAndLinearity, "lightStrengthAndLinearity", UniformType.Vec2fArray);
            DeclareUniform(UniformName.useLightStrength, "useLightStrength", UniformType.Float);
            
            // volte: classic rendering
            DeclareUniform(UniformName.drawPaletted, "drawPaletted", UniformType.Int);
            DeclareUniform(UniformName.colormapSize, "colormapSize", UniformType.Vec2i);
            DeclareUniform(UniformName.doomlightlevels, "doomlightlevels", UniformType.Int);
            DeclareUniform(UniformName.sectorLightLevel, "sectorLightLevel", UniformType.Int);

            DeclareUniform(UniformName.skew, "skew", UniformType.Vec2f);

            // 2d fsaa
            CompileShader(ShaderName.display2d_fsaa, "display2d.shader", "display2d_fsaa");
            
            // 2d normal
            CompileShader(ShaderName.display2d_normal, "display2d.shader", "display2d_normal");
            CompileShader(ShaderName.display2d_fullbright, "display2d.shader", "display2d_fullbright");

            // 2d things
            CompileShader(ShaderName.things2d_thing, "things2d.shader", "things2d_thing");
            CompileShader(ShaderName.things2d_sprite, "things2d.shader", "things2d_sprite");
            CompileShader(ShaderName.things2d_fill, "things2d.shader", "things2d_fill");

            // non-fog 3d shaders
            CompileShader(ShaderName.world3d_main, "world3d.shader", "world3d_main");
            CompileShader(ShaderName.world3d_fullbright, "world3d.shader", "world3d_fullbright");
            CompileShader(ShaderName.world3d_main_highlight, "world3d.shader", "world3d_main_highlight");
            CompileShader(ShaderName.world3d_fullbright_highlight, "world3d.shader", "world3d_fullbright_highlight");
            CompileShader(ShaderName.world3d_vertex_color, "world3d.shader", "world3d_vertex_color");
            CompileShader(ShaderName.world3d_main_vertexcolor, "world3d.shader", "world3d_main_vertexcolor");
            CompileShader(ShaderName.world3d_constant_color, "world3d.shader", "world3d_constant_color");
            
            // classic rendering
            CompileShader(ShaderName.world3d_classic, "world3d.shader", "world3d_classic");
            CompileShader(ShaderName.world3d_classic_highlight, "world3d.shader", "world3d_classic_highlight");

			// skybox shader
			CompileShader(ShaderName.world3d_skybox, "world3d_skybox.shader", "world3d_skybox");

            // fog 3d shaders
            CompileShader(ShaderName.world3d_main_fog, "world3d.shader", "world3d_main_fog");
            CompileShader(ShaderName.world3d_main_highlight_vertexcolor, "world3d.shader", "world3d_highlight_vertexcolor");
            CompileShader(ShaderName.world3d_main_highlight_fog, "world3d.shader", "world3d_main_highlight_fog");
            CompileShader(ShaderName.world3d_main_fog_vertexcolor, "world3d.shader", "world3d_main_fog_vertexcolor");
            CompileShader(ShaderName.world3d_main_highlight_fog_vertexcolor, "world3d.shader", "world3d_main_highlight_fog_vertexcolor");

			// Slope handle
			CompileShader(ShaderName.world3d_slope_handle, "world3d.shader", "world3d_slope_handle");

            SetupSettings();
        }

        ~RenderDevice()
        {
            Dispose();
        }

        void CreateDevice()
        {
            // Grab the X11 Display handle by abusing reflection to access internal classes in the mono implementation.
            // That's par for the course for everything in Linux, so yeah..
            IntPtr display = IntPtr.Zero;
            Type xplatui = Type.GetType("System.Windows.Forms.XplatUIX11, System.Windows.Forms");
            if (xplatui != null)
            {
                display = (IntPtr)xplatui.GetField("DisplayHandle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            }

            Handle = RenderDevice_New(display, RenderTarget.Handle, General.DebugRenderDevice);
            if (Handle == IntPtr.Zero)
            {
                StringBuilder sb = new StringBuilder(4096);
                BuilderNative_GetError(sb, sb.Capacity);
                throw new RenderDeviceException(string.Format("Could not create render device: {0}", sb));
            }
        }

        public bool Disposed { get { return Handle == IntPtr.Zero; } }

        void ThrowIfFailed(bool result)
        {
            if (!result)
            {
                StringBuilder sb = new StringBuilder(4096);
                BuilderNative_GetError(sb, sb.Capacity);
                throw new RenderDeviceException(sb.ToString());
            }
        }

        public void Dispose()
        {
            if (!Disposed)
            {
                RenderDevice_Delete(Handle);
                Handle = IntPtr.Zero;
            }
        }

        public void DeclareUniform(UniformName name, string variablename, UniformType type)
        {
            RenderDevice_DeclareUniform(Handle, name, variablename, type);
        }

        public void DeclareShader(ShaderName name, string vertResourceName, string fragResourceName)
        {
            RenderDevice_DeclareShader(Handle, name, name.ToString(), GetResourceText(vertResourceName), GetResourceText(fragResourceName));
        }

        // Save parsed shader groups -- don't parse the same embedded source once for
        // every shader variant or every map opened during this process.
        private static readonly Dictionary<string, ShaderGroup> precompiledGroups = new Dictionary<string, ShaderGroup>();
        public void CompileShader(ShaderName internalName, string groupName, string shaderName)
        {
            ShaderGroup sg;

            if (precompiledGroups.ContainsKey(groupName))
                sg = precompiledGroups[groupName];
            else
            {
                sg = ShaderCompiler.Compile(GetResourceText(groupName));
                precompiledGroups.Add(groupName, sg);
            }

            Shader s = sg.GetShader(shaderName);

            if (s == null)
                throw new RenderDeviceException(string.Format("Shader {0}::{1} not found", groupName, shaderName));

            /*General.WriteLogLine(string.Format("===========================================\nDBG: loading shader {0} / {1}\n\nVertex source: {2}\n\nFragment source: {3}\n\n===========================================",
                groupName, shaderName, s.GetVertexSource(), s.GetFragmentSource()));*/
            RenderDevice_DeclareShader(Handle, internalName, internalName.ToString(), s.GetVertexSource(), s.GetFragmentSource());
        }

        static string GetResourceText(string name)
        {
            string fullname = string.Format("CodeImp.DoomBuilder.Resources.{0}", name);
            using (Stream stream = General.ThisAssembly.GetManifestResourceStream(fullname))
            {
                if (stream == null)
                    throw new Exception(string.Format("Resource {0} not found!", fullname));
                byte[] data = new byte[stream.Length];
                if (stream.Read(data, 0, data.Length) != data.Length)
                    throw new Exception("Could not read resource stream");
                int start = 0;
                if (data.Length >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf)
                    start = 3;
                return Encoding.UTF8.GetString(data, start, data.Length - start);
            }
        }

        public void SetShader(ShaderName shader)
        {
			if(managedshader.HasValue && managedshader.Value == shader) return;
			managedshader = shader;
            RenderDevice_SetShader(Handle, shader);
        }

		private bool FloatUniformUnchanged(UniformName uniform, int components)
		{
			int index = (int)uniform;
			int offset = index * 4;
			bool unchanged = uniformfloatcachecomponents[index] == components;
			for(int i = 0; unchanged && i < components; i++)
				unchanged = uniformfloatcache[offset + i].Equals(uniformfloatbuffer[i]);
			if(unchanged) return true;
			for(int i = 0; i < components; i++) uniformfloatcache[offset + i] = uniformfloatbuffer[i];
			uniformfloatcachecomponents[index] = (byte)components;
			return false;
		}

		private bool IntUniformUnchanged(UniformName uniform, int components)
		{
			int index = (int)uniform;
			int offset = index * 4;
			bool unchanged = uniformintcachecomponents[index] == components;
			for(int i = 0; unchanged && i < components; i++)
				unchanged = uniformintcache[offset + i] == uniformintbuffer[i];
			if(unchanged) return true;
			for(int i = 0; i < components; i++) uniformintcache[offset + i] = uniformintbuffer[i];
			uniformintcachecomponents[index] = (byte)components;
			return false;
		}

		private static bool MatricesEqual(ref Matrix left, ref Matrix right)
		{
			return left.M11.Equals(right.M11) && left.M12.Equals(right.M12) && left.M13.Equals(right.M13) && left.M14.Equals(right.M14)
				&& left.M21.Equals(right.M21) && left.M22.Equals(right.M22) && left.M23.Equals(right.M23) && left.M24.Equals(right.M24)
				&& left.M31.Equals(right.M31) && left.M32.Equals(right.M32) && left.M33.Equals(right.M33) && left.M34.Equals(right.M34)
				&& left.M41.Equals(right.M41) && left.M42.Equals(right.M42) && left.M43.Equals(right.M43) && left.M44.Equals(right.M44);
		}

		private bool MatrixUniformUnchanged(UniformName uniform, ref Matrix value)
		{
			int index = (int)uniform;
			if(uniformmatrixcachevalid[index] && MatricesEqual(ref uniformmatrixcache[index], ref value)) return true;
			uniformmatrixcache[index] = value;
			uniformmatrixcachevalid[index] = true;
			return false;
		}

        public void SetUniform(UniformName uniform, bool value)
        {
			uniformfloatbuffer[0] = value ? 1.0f : 0.0f;
			if(FloatUniformUnchanged(uniform, 1)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float));
        }
        public void SetUniform(UniformName uniform, float value)
        {
			uniformfloatbuffer[0] = value;
			if(FloatUniformUnchanged(uniform, 1)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float));
        }
        public void SetUniform(UniformName uniform, Vector2f value)
        {
			uniformfloatbuffer[0] = value.X;
			uniformfloatbuffer[1] = value.Y;
			if(FloatUniformUnchanged(uniform, 2)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float) * 2);
        }
        public void SetUniform(UniformName uniform, Vector3f value)
        {
			uniformfloatbuffer[0] = value.X;
			uniformfloatbuffer[1] = value.Y;
			uniformfloatbuffer[2] = value.Z;
			if(FloatUniformUnchanged(uniform, 3)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float) * 3);
        }
        public void SetUniform(UniformName uniform, Vector4f value)
        {
			uniformfloatbuffer[0] = value.X;
			uniformfloatbuffer[1] = value.Y;
			uniformfloatbuffer[2] = value.Z;
			uniformfloatbuffer[3] = value.W;
			if(FloatUniformUnchanged(uniform, 4)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float) * 4);
        }
        public void SetUniform(UniformName uniform, Color4 value)
        {
			uniformfloatbuffer[0] = value.Red;
			uniformfloatbuffer[1] = value.Green;
			uniformfloatbuffer[2] = value.Blue;
			uniformfloatbuffer[3] = value.Alpha;
			if(FloatUniformUnchanged(uniform, 4)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformfloatbuffer, 1, sizeof(float) * 4);
        }

        public void SetUniform(UniformName uniform, Matrix matrix)
        {
			if(MatrixUniformUnchanged(uniform, ref matrix)) return;
            RenderDevice_SetUniform(Handle, uniform, ref matrix, 1, sizeof(float) * 16);
        }

        public void SetUniform(UniformName uniform, ref Matrix matrix)
        {
			if(MatrixUniformUnchanged(uniform, ref matrix)) return;
            RenderDevice_SetUniform(Handle, uniform, ref matrix, 1, sizeof(float) * 16);
        }

        public void SetUniform(UniformName uniform, int value)
        {
			uniformintbuffer[0] = value;
			if(IntUniformUnchanged(uniform, 1)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformintbuffer, 1, sizeof(int));
        }
        public void SetUniform(UniformName uniform, Vector2i value)
        {
			uniformintbuffer[0] = value.X;
			uniformintbuffer[1] = value.Y;
			if(IntUniformUnchanged(uniform, 2)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformintbuffer, 1, sizeof(int) * 2);
        }
        public void SetUniform(UniformName uniform, Vector3i value)
        {
			uniformintbuffer[0] = value.X;
			uniformintbuffer[1] = value.Y;
			uniformintbuffer[2] = value.Z;
			if(IntUniformUnchanged(uniform, 3)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformintbuffer, 1, sizeof(int) * 3);
        }
        public void SetUniform(UniformName uniform, Vector4i value)
        {
			uniformintbuffer[0] = value.X;
			uniformintbuffer[1] = value.Y;
			uniformintbuffer[2] = value.Z;
			uniformintbuffer[3] = value.W;
			if(IntUniformUnchanged(uniform, 4)) return;
            RenderDevice_SetUniform(Handle, uniform, uniformintbuffer, 1, sizeof(int) * 4);
        }
        public void SetUniform(UniformName uniform, Vector2f[] value)
        {
			int requiredlength = value.Length * 2;
			if(uniformarraybuffer.Length < requiredlength) uniformarraybuffer = new float[requiredlength];
            float[] conv = uniformarraybuffer;
            int cv = 0;
			for(int i = 0; i < requiredlength; i += 2)
            {
                conv[i] = value[cv].X;
                conv[i + 1] = value[cv].Y;
                cv++;
            }
			RenderDevice_SetUniform(Handle, uniform, conv, value.Length, sizeof(float) * requiredlength);
        }
        public void SetUniform(UniformName uniform, Vector3f[] value)
        {
			int requiredlength = value.Length * 3;
			if(uniformarraybuffer.Length < requiredlength) uniformarraybuffer = new float[requiredlength];
            float[] conv = uniformarraybuffer;
            int cv = 0;
			for(int i = 0; i < requiredlength; i += 3)
            {
                conv[i] = value[cv].X;
                conv[i + 1] = value[cv].Y;
                conv[i + 2] = value[cv].Z;
                cv++;
            }
			RenderDevice_SetUniform(Handle, uniform, conv, value.Length, sizeof(float) * requiredlength);
        }
        public void SetUniform(UniformName uniform, Vector4f[] value)
        {
			int requiredlength = value.Length * 4;
			if(uniformarraybuffer.Length < requiredlength) uniformarraybuffer = new float[requiredlength];
            float[] conv = uniformarraybuffer;
            int cv = 0;
			for(int i = 0; i < requiredlength; i += 4)
            {
                conv[i] = value[cv].X;
                conv[i + 1] = value[cv].Y;
                conv[i + 2] = value[cv].Z;
                conv[i + 3] = value[cv].W;
                cv++;
            }
			RenderDevice_SetUniform(Handle, uniform, conv, value.Length, sizeof(float) * requiredlength);
        }

        public void SetVertexBuffer(VertexBuffer buffer)
        {
			if(managedvertexbuffervalid && object.ReferenceEquals(managedvertexbuffer, buffer)
				&& (buffer == null || !buffer.Disposed)) return;
			managedvertexbuffer = buffer;
			managedvertexbuffervalid = true;
            RenderDevice_SetVertexBuffer(Handle, buffer != null ? buffer.Handle : IntPtr.Zero);
        }

        public void SetIndexBuffer(IndexBuffer buffer)
        {
			if(managedindexbuffervalid && object.ReferenceEquals(managedindexbuffer, buffer)
				&& (buffer == null || !buffer.Disposed)) return;
			managedindexbuffer = buffer;
			managedindexbuffervalid = true;
            RenderDevice_SetIndexBuffer(Handle, buffer != null ? buffer.Handle : IntPtr.Zero);
        }

        public void SetAlphaBlendEnable(bool value)
        {
			if(managedalphablendenable.HasValue && managedalphablendenable.Value == value) return;
			managedalphablendenable = value;
            RenderDevice_SetAlphaBlendEnable(Handle, value);
        }

        public void SetAlphaTestEnable(bool value)
        {
			if(managedalphatestenable.HasValue && managedalphatestenable.Value == value) return;
			managedalphatestenable = value;
            RenderDevice_SetAlphaTestEnable(Handle, value);
        }

        public void SetCullMode(Cull mode)
        {
			if(managedcullmode.HasValue && managedcullmode.Value == mode) return;
			managedcullmode = mode;
            RenderDevice_SetCullMode(Handle, mode);
        }

        public void SetBlendOperation(BlendOperation op)
        {
			if(managedblendoperation.HasValue && managedblendoperation.Value == op) return;
			managedblendoperation = op;
            RenderDevice_SetBlendOperation(Handle, op);
        }

        public void SetSourceBlend(Blend blend)
        {
			if(managedsourceblend.HasValue && managedsourceblend.Value == blend) return;
			managedsourceblend = blend;
            RenderDevice_SetSourceBlend(Handle, blend);
        }

        public void SetDestinationBlend(Blend blend)
        {
			if(manageddestinationblend.HasValue && manageddestinationblend.Value == blend) return;
			manageddestinationblend = blend;
            RenderDevice_SetDestinationBlend(Handle, blend);
        }

        public void SetFillMode(FillMode mode)
        {
			if(managedfillmode.HasValue && managedfillmode.Value == mode) return;
			managedfillmode = mode;
            RenderDevice_SetFillMode(Handle, mode);
        }

        public void SetMultisampleAntialias(bool value)
        {
			if(managedmultisampleantialias.HasValue && managedmultisampleantialias.Value == value) return;
			managedmultisampleantialias = value;
            RenderDevice_SetMultisampleAntialias(Handle, value);
        }

        public void SetZEnable(bool value)
        {
			if(managedzenable.HasValue && managedzenable.Value == value) return;
			managedzenable = value;
            RenderDevice_SetZEnable(Handle, value);
        }

        public void SetZWriteEnable(bool value)
        {
			if(managedzwriteenable.HasValue && managedzwriteenable.Value == value) return;
			managedzwriteenable = value;
            RenderDevice_SetZWriteEnable(Handle, value);
        }

        public void SetTexture(BaseTexture value, int unit = 0)
        {
			if(unit >= 0 && unit < managedtextureunits)
			{
				if(managedtexturevalid[unit] && object.ReferenceEquals(managedtextures[unit], value)
					&& (value == null || !value.Disposed)) return;
				managedtextures[unit] = value;
				managedtexturevalid[unit] = true;
			}
            RenderDevice_SetTexture(Handle, unit, value != null ? value.Handle : IntPtr.Zero);
        }

        public void SetSamplerFilter(TextureFilter filter, int unit = 0)
        {
            SetSamplerFilter(filter, filter, MipmapFilter.None, 0.0f, unit);
        }

        public void SetSamplerFilter(TextureFilter minfilter, TextureFilter magfilter, MipmapFilter mipfilter, float maxanisotropy, int unit = 0)
        {
			if(unit >= 0 && unit < managedtextureunits)
			{
				if(managedsamplerfiltervalid[unit]
					&& managedsamplerminfilter[unit].Value == minfilter
					&& managedsamplermagfilter[unit].Value == magfilter
					&& managedsamplermipfilter[unit].Value == mipfilter
					&& managedsampleranisotropy[unit] == maxanisotropy) return;

				managedsamplerminfilter[unit] = minfilter;
				managedsamplermagfilter[unit] = magfilter;
				managedsamplermipfilter[unit] = mipfilter;
				managedsampleranisotropy[unit] = maxanisotropy;
				managedsamplerfiltervalid[unit] = true;
			}
            RenderDevice_SetSamplerFilter(Handle, unit, minfilter, magfilter, mipfilter, maxanisotropy);
        }

        public void SetSamplerState(TextureAddress address, int unit = 0)
        {
			if(unit >= 0 && unit < managedtextureunits)
			{
				if(managedsampleraddress[unit].HasValue && managedsampleraddress[unit].Value == address) return;
				managedsampleraddress[unit] = address;
			}
            RenderDevice_SetSamplerState(Handle, unit, address);
        }

        public void DrawIndexed(PrimitiveType type, int startIndex, int primitiveCount)
        {
            ThrowIfFailed(RenderDevice_DrawIndexed(Handle, type, startIndex, primitiveCount));
        }

        public void Draw(PrimitiveType type, int startIndex, int primitiveCount)
        {
            ThrowIfFailed(RenderDevice_Draw(Handle, type, startIndex, primitiveCount));
        }

        public void Draw(PrimitiveType type, int startIndex, int primitiveCount, FlatVertex[] data)
        {
            ThrowIfFailed(RenderDevice_DrawData(Handle, type, startIndex, primitiveCount, data));
        }

        public void StartRendering(bool clear, Color4 backcolor)
        {
            ThrowIfFailed(RenderDevice_StartRendering(Handle, clear, backcolor.ToArgb(), IntPtr.Zero, true));
        }

        public void StartRendering(bool clear, Color4 backcolor, Texture target, bool usedepthbuffer)
        {
            ThrowIfFailed(RenderDevice_StartRendering(Handle, clear, backcolor.ToArgb(), target.Handle, usedepthbuffer));
        }

        public void FinishRendering()
        {
            ThrowIfFailed(RenderDevice_FinishRendering(Handle));
        }

        public void Present()
        {
            ThrowIfFailed(RenderDevice_Present(Handle));

			// BuilderNative leaves the OpenGL context current after Present. Applying
			// the swap interval here (instead of only during device setup) makes the
			// VSync setting reach the actual rendering context on Windows.
			ApplySwapInterval();
        }

        public void ClearTexture(Color4 backcolor, Texture texture)
        {
            ThrowIfFailed(RenderDevice_ClearTexture(Handle, backcolor.ToArgb(), texture.Handle));
        }

        public void CopyTexture(CubeTexture dst, CubeMapFace face)
        {
            ThrowIfFailed(RenderDevice_CopyTexture(Handle, dst.Handle, face));
        }

        public void SetBufferData(IndexBuffer buffer, int[] data)
        {
            ThrowIfFailed(RenderDevice_SetIndexBufferData(Handle, buffer.Handle, data, data.Length * Marshal.SizeOf<int>()));
        }

        public void SetBufferData(VertexBuffer buffer, int length, VertexFormat format)
        {
            int stride = (format == VertexFormat.Flat) ? FlatVertex.Stride : WorldVertex.Stride;
            ThrowIfFailed(RenderDevice_SetVertexBufferData(Handle, buffer.Handle, IntPtr.Zero, length * stride, format));
        }

        public void SetBufferData(VertexBuffer buffer, FlatVertex[] data)
        {
            ThrowIfFailed(RenderDevice_SetVertexBufferData(Handle, buffer.Handle, data, data.Length * Marshal.SizeOf<FlatVertex>(), VertexFormat.Flat));
        }

        public void SetBufferData(VertexBuffer buffer, WorldVertex[] data)
        {
            ThrowIfFailed(RenderDevice_SetVertexBufferData(Handle, buffer.Handle, data, data.Length * Marshal.SizeOf<WorldVertex>(), VertexFormat.World));
        }

        public void SetBufferSubdata(VertexBuffer buffer, long destOffset, FlatVertex[] data)
        {
            ThrowIfFailed(RenderDevice_SetVertexBufferSubdata(Handle, buffer.Handle, destOffset * FlatVertex.Stride, data, data.Length * FlatVertex.Stride));
        }

        public void SetBufferSubdata(VertexBuffer buffer, long destOffset, WorldVertex[] data)
        {
            ThrowIfFailed(RenderDevice_SetVertexBufferSubdata(Handle, buffer.Handle, destOffset * WorldVertex.Stride, data, data.Length * WorldVertex.Stride));
        }

        public void SetBufferSubdata(VertexBuffer buffer, FlatVertex[] data, long size)
        {
            if (size < 0 || size > data.Length) throw new ArgumentOutOfRangeException("size");
            ThrowIfFailed(RenderDevice_SetVertexBufferSubdata(Handle, buffer.Handle, 0, data, size * FlatVertex.Stride));
        }

        public void SetPixels(Texture texture, System.Drawing.Bitmap bitmap)
        {
            System.Drawing.Imaging.BitmapData bmpdata = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Size.Width, bitmap.Size.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                ThrowIfFailed(RenderDevice_SetPixels(Handle, texture.Handle, bmpdata.Scan0));
            }
            finally
            {
                bitmap.UnlockBits(bmpdata);
            }
        }

        public void SetPixels(CubeTexture texture, CubeMapFace face, System.Drawing.Bitmap bitmap)
        {
            System.Drawing.Imaging.BitmapData bmpdata = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Size.Width, bitmap.Size.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                ThrowIfFailed(RenderDevice_SetCubePixels(Handle, texture.Handle, face, bmpdata.Scan0));
            }
            finally
            {
                bitmap.UnlockBits(bmpdata);
            }
        }

        public unsafe void SetPixels(Texture texture, uint* pixeldata)
        {
            ThrowIfFailed(RenderDevice_SetPixels(Handle, texture.Handle, new IntPtr(pixeldata)));
        }

        public unsafe void* MapPBO(Texture texture)
        {
            void* ptr = RenderDevice_MapPBO(Handle, texture.Handle).ToPointer();
            ThrowIfFailed(ptr != null);
            return ptr;
        }

        public void UnmapPBO(Texture texture)
        {
            ThrowIfFailed(RenderDevice_UnmapPBO(Handle, texture.Handle));
        }

        internal void RegisterResource(IRenderResource res)
        {
        }

        internal void UnregisterResource(IRenderResource res)
        {
        }

		private void InvalidateManagedRenderStateCache()
		{
			managedalphablendenable = null;
			managedalphatestenable = null;
			managedcullmode = null;
			managedblendoperation = null;
			managedsourceblend = null;
			manageddestinationblend = null;
			managedfillmode = null;
			managedmultisampleantialias = null;
			managedzenable = null;
			managedzwriteenable = null;

			for(int i = 0; i < managedtextureunits; i++)
			{
				managedsampleraddress[i] = null;
				managedsamplerminfilter[i] = null;
				managedsamplermagfilter[i] = null;
				managedsamplermipfilter[i] = null;
				managedsamplerfiltervalid[i] = false;
			}
		}

        public void SetupSettings()
		{
			// Settings can be reapplied after preferences or device state changes. Force
			// one complete native refresh, then resume filtering duplicate calls.
			InvalidateManagedRenderStateCache();

			// A settings change is an explicit request to retry the selected VSync mode.
			highFrameRateVSyncBypass = false;
			appliedSwapInterval = int.MinValue;

			// Setup renderstates
			SetAlphaBlendEnable(false);
			SetAlphaTestEnable(false);
			SetCullMode(Cull.None);
			SetDestinationBlend(Blend.InverseSourceAlpha);
			SetFillMode(FillMode.Solid);
			SetMultisampleAntialias((General.Settings.AntiAliasingSamples > 0));
			SetSourceBlend(Blend.SourceAlpha);
			SetZEnable(false);
			SetZWriteEnable(false);
			
			// Texture addressing
			SetSamplerState(TextureAddress.Wrap);
			
            //mxd. It's still nice to have anisotropic filtering when texture filtering is disabled
            TextureFilter magminfilter = (General.Settings.VisualBilinear ? TextureFilter.Linear : TextureFilter.Nearest);
            SetSamplerFilter(
                magminfilter,
                magminfilter,
                General.Settings.VisualBilinear ? MipmapFilter.Linear : MipmapFilter.Nearest,
                General.Settings.FilterAnisotropy);

            // Initialize presentations
            Presentation.Initialize();

            ApplySwapInterval();
		}

		// WGL defaults and driver profiles can synchronize SwapBuffers to the monitor.
		// Make that behavior explicit so the Visual Mode FPS limiter can operate above
		// 60 Hz when requested. Driver-forced VSync may still override this setting.
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate bool WglSwapIntervalDelegate(int interval);

		[DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
		private static extern IntPtr wglGetProcAddress(string functionName);

		[DllImport("opengl32.dll")]
		private static extern IntPtr wglGetCurrentContext();

		private static WglSwapIntervalDelegate wglSwapInterval;
		private static int appliedSwapInterval = int.MinValue;
		private static int failedSwapInterval = int.MinValue;
		private static bool highFrameRateVSyncBypass;

		/// <summary>
		/// Uses the application's high-resolution frame cap when a driver/windowed
		/// OpenGL VSync wait is demonstrably holding a high-FPS request near 60 Hz.
		/// </summary>
		public bool UseSoftwareFramePacingForHighFrameRate()
		{
			if(!General.Settings.VisualModeVSync || General.Settings.VisualModeFPSLimit < 100
				|| highFrameRateVSyncBypass) return false;

			highFrameRateVSyncBypass = true;
			appliedSwapInterval = int.MinValue;
			ApplySwapInterval();
			return appliedSwapInterval == 0;
		}

		private static void ApplySwapInterval()
		{
			if(Environment.OSVersion.Platform != PlatformID.Win32NT) return;

			int requestedinterval = (General.Settings.VisualModeVSync && !highFrameRateVSyncBypass ? 1 : 0);
			// Present calls this every frame so the first frame with a current GL context
			// can apply VSync. Once applied (or rejected), avoid even the WGL context query.
			if(appliedSwapInterval == requestedinterval || failedSwapInterval == requestedinterval) return;

			try
			{
				// wglGetProcAddress only returns extension entry points for a current
				// context. SetupSettings can run before BuilderNative makes it current,
				// so leave the request pending for the first Present call in that case.
				if(wglGetCurrentContext() == IntPtr.Zero) return;

				if(wglSwapInterval == null)
				{
					IntPtr address = wglGetProcAddress("wglSwapIntervalEXT");
					long value = address.ToInt64();
					if(address != IntPtr.Zero && value != 1 && value != 2 && value != 3 && value != -1)
						wglSwapInterval = (WglSwapIntervalDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(WglSwapIntervalDelegate));
				}

				if(wglSwapInterval != null)
				{
					if(wglSwapInterval(requestedinterval))
					{
						appliedSwapInterval = requestedinterval;
						failedSwapInterval = int.MinValue;
						General.WriteLogLine("OpenGL swap interval set to " + requestedinterval
							+ (requestedinterval == 0
								? (highFrameRateVSyncBypass ? " (adaptive high-FPS software pacing)." : " (VSync off).")
								: " (VSync on)."));
					}
					else
					{
						failedSwapInterval = requestedinterval;
						General.WriteLogLine("OpenGL driver rejected swap interval " + requestedinterval + ".");
					}
				}
			}
			catch(DllNotFoundException) { }
			catch(EntryPointNotFoundException) { }
		}

        IntPtr Handle;

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr RenderDevice_New(IntPtr display, IntPtr window, bool debug);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_Delete(IntPtr handle);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void RenderDevice_DeclareUniform(IntPtr handle, UniformName name, string variablename, UniformType type);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void RenderDevice_DeclareShader(IntPtr handle, ShaderName index, string name, string vertexShader, string fragShader);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern void BuilderNative_GetError(StringBuilder str, int length);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetShader(IntPtr handle, ShaderName name);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetUniform(IntPtr handle, UniformName name, int[] data, int count, int bytesize);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetUniform(IntPtr handle, UniformName name, float[] data, int count, int bytesize);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetUniform(IntPtr handle, UniformName name, ref Matrix data, int count, int bytesize);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetVertexBuffer(IntPtr handle, IntPtr buffer);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetIndexBuffer(IntPtr handle, IntPtr buffer);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetAlphaBlendEnable(IntPtr handle, bool value);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetAlphaTestEnable(IntPtr handle, bool value);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetCullMode(IntPtr handle, Cull mode);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetBlendOperation(IntPtr handle, BlendOperation op);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetSourceBlend(IntPtr handle, Blend blend);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetDestinationBlend(IntPtr handle, Blend blend);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetFillMode(IntPtr handle, FillMode mode);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetMultisampleAntialias(IntPtr handle, bool value);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetZEnable(IntPtr handle, bool value);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetZWriteEnable(IntPtr handle, bool value);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetTexture(IntPtr handle, int unit, IntPtr texture);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetSamplerFilter(IntPtr handle, int unit, TextureFilter minfilter, TextureFilter magfilter, MipmapFilter mipfilter, float maxanisotropy);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern void RenderDevice_SetSamplerState(IntPtr handle, int unit, TextureAddress address);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_Draw(IntPtr handle, PrimitiveType type, int startIndex, int primitiveCount);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_DrawIndexed(IntPtr handle, PrimitiveType type, int startIndex, int primitiveCount);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_DrawData(IntPtr handle, PrimitiveType type, int startIndex, int primitiveCount, FlatVertex[] data);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_StartRendering(IntPtr handle, bool clear, int backcolor, IntPtr target, bool usedepthbuffer);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_FinishRendering(IntPtr handle);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_Present(IntPtr handle);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_ClearTexture(IntPtr handle, int backcolor, IntPtr texture);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_CopyTexture(IntPtr handle, IntPtr dst, CubeMapFace face);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetIndexBufferData(IntPtr handle, IntPtr buffer, int[] data, long size);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetVertexBufferData(IntPtr handle, IntPtr buffer, IntPtr data, long size, VertexFormat format);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetVertexBufferData(IntPtr handle, IntPtr buffer, FlatVertex[] data, long size, VertexFormat format);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetVertexBufferData(IntPtr handle, IntPtr buffer, WorldVertex[] data, long size, VertexFormat format);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetVertexBufferSubdata(IntPtr handle, IntPtr buffer, long destOffset, FlatVertex[] data, long sizeInBytes);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        static extern bool RenderDevice_SetVertexBufferSubdata(IntPtr handle, IntPtr buffer, long destOffset, WorldVertex[] data, long sizeInBytes);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        protected static extern bool RenderDevice_SetPixels(IntPtr handle, IntPtr texture, IntPtr data);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        protected static extern IntPtr RenderDevice_MapPBO(IntPtr handle, IntPtr texture);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        protected static extern bool RenderDevice_UnmapPBO(IntPtr handle, IntPtr texture);

        [DllImport("BuilderNative", CallingConvention = CallingConvention.Cdecl)]
        protected static extern bool RenderDevice_SetCubePixels(IntPtr handle, IntPtr texture, CubeMapFace face, IntPtr data);

        //mxd. Anisotropic filtering steps
        public static readonly List<float> AF_STEPS = new List<float> { 1.0f, 2.0f, 4.0f, 8.0f, 16.0f };

        //mxd. Antialiasing steps
        public static readonly List<int> AA_STEPS = new List<int> { 0, 2, 4, 8 };

        internal RenderTargetControl RenderTarget { get; private set; }

		// This makes a Vector3 from Vector3D
		public static Vector3f V3(Vector3D v3d)
		{
			return new Vector3f((float)v3d.x, (float)v3d.y, (float)v3d.z);
		}

		// This makes a Vector3D from Vector3
		public static Vector3D V3D(Vector3f v3)
		{
			return new Vector3D(v3.X, v3.Y, v3.Z);
		}

		// This makes a Vector2 from Vector2D
		public static Vector2f V2(Vector2D v2d)
		{
			return new Vector2f((float)v2d.x, (float)v2d.y);
		}

		// This makes a Vector2D from Vector2
		public static Vector2D V2D(Vector2f v2)
		{
			return new Vector2D(v2.X, v2.Y);
		}
    }

    public enum ShaderName : int
    {
        display2d_fsaa,
        display2d_normal,
        display2d_fullbright,
        things2d_thing,
        things2d_sprite,
        things2d_fill,
        world3d_main,
        world3d_fullbright,
        world3d_main_highlight,
        world3d_fullbright_highlight,
        world3d_main_vertexcolor,
        world3d_skybox,
        world3d_main_highlight_vertexcolor,
        world3d_p7,
        world3d_main_fog,
        world3d_p9,
        world3d_main_highlight_fog,
        world3d_p11,
        world3d_main_fog_vertexcolor,
        world3d_p13,
        world3d_main_highlight_fog_vertexcolor,
        world3d_vertex_color,
        world3d_constant_color,
		world3d_slope_handle,
        world3d_classic,
        world3d_p19,
		world3d_classic_highlight,
	}

    public enum UniformType : int
    {
        Vec4f,
        Vec3f,
        Vec2f,
        Float,
        Mat4,
        Vec4i,
        Vec3i,
        Vec2i,
        Int,
        Vec4fArray,
        Vec3fArray,
        Vec2fArray
    }

    public enum UniformName : int
    {
        rendersettings,
        projection,
        desaturation,
        highlightcolor,
        view,
        world,
        modelnormal,
        FillColor,
        vertexColor,
        stencilColor,
        lightPosAndRadius,
        lightOrientation,
        light2Radius,
        lightColor,
        ignoreNormals,
        spotLight,
        campos,
        fogsettings,
        fogcolor,
        sectorfogcolor,
        lightsEnabled,
		slopeHandleLength,
        drawPaletted,
        colormapSize,
        sectorLightLevel,
        doomlightlevels,
        skew,
		lightStrengthAndLinearity,
		useLightStrength
    }

    public enum VertexFormat : int { Flat, World }
    public enum Cull : int { None, Clockwise }
    public enum Blend : int { InverseSourceAlpha, SourceAlpha, One }
    public enum BlendOperation : int { Add, ReverseSubtract }
    public enum FillMode : int { Solid, Wireframe }
    public enum TextureAddress : int { Wrap, Clamp }
    public enum PrimitiveType : int { LineList, TriangleList, TriangleStrip }
    public enum TextureFilter : int { Nearest, Linear }
    public enum MipmapFilter : int { None, Nearest, Linear}
}
