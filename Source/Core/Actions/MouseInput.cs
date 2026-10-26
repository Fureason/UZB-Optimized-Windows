
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
using System.Windows.Forms;
using CodeImp.DoomBuilder.Geometry;
using System.Runtime.InteropServices;
using System.Drawing;

#endregion

namespace CodeImp.DoomBuilder.Actions
{
	internal class MouseInput : IDisposable
	{
		#region ================== Variables

		// Mouse input
		private RawMouse mouse;
		private bool firstProcess = true;
		private bool processing;
		private bool disposed;
		private Point lastPos = new Point();
		private Control source;

		private const uint WM_INPUT = 0x00FF;
		private const uint PM_REMOVE = 0x0001;

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeMessage
		{
			public IntPtr hwnd;
			public uint message;
			public UIntPtr wParam;
			public IntPtr lParam;
			public uint time;
			public Point pt;
			public uint lPrivate;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool PeekMessage(out NativeMessage message, IntPtr window,
			uint minimum, uint maximum, uint remove);

		[DllImport("user32.dll")]
		private static extern IntPtr DispatchMessage(ref NativeMessage message);
		
		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public MouseInput(Control source)
		{
			this.source = source;

			// Start mouse input
			try
			{
				mouse = new RawMouse(source);
			}
			catch
			{
				mouse = null;
			}

			#if MONO_WINFORMS
			MouseInput_ShowCursor(false);
			#endif

			// We have no destructor
			GC.SuppressFinalize(this);
		}

		// Disposer
		public void Dispose()
		{
			if(disposed) return;
			disposed = true;

			// Clear the field before destroying the native input window. DestroyWindow
			// can dispatch focus/window messages, so a nested frame must not see this
			// MouseInput as usable while Alt+Tab is tearing it down.
			RawMouse oldmouse = mouse;
			mouse = null;
			if(oldmouse != null) oldmouse.Dispose();

			#if MONO_WINFORMS
			MouseInput_ShowCursor(true);
			#endif
		}

		#endregion

		#region ================== Methods

		#endregion

		#region ================== Processing

		// Rendering a very large view can keep the UI thread busy long enough for raw
		// mouse messages to accumulate. Process every pending delta before polling the
		// native accumulator, so old motion is coalesced into this frame instead of
		// being replayed for several seconds after the user stops moving the mouse.
		private static void DrainPendingRawMouseInput()
		{
			NativeMessage message;
			while(PeekMessage(out message, IntPtr.Zero, WM_INPUT, WM_INPUT, PM_REMOVE))
				DispatchMessage(ref message);
		}

		// This processes the input
		public Vector2D Process()
		{
			// ProcessFrame can be entered again while pending native input is dispatched.
			// Focus loss disposes this object, so nested or post-disposal processing must
			// return a neutral delta instead of touching a released RawMouse instance.
			if(disposed || processing) return new Vector2D();

			processing = true;
			try
			{
				float msX, msY;
				RawMouse currentmouse = mouse;
				if(currentmouse != null) // Windows version where RawInput is available
				{
					DrainPendingRawMouseInput();

					// Dispatching WM_INPUT can indirectly process an Alt+Tab/deactivate
					// message and dispose the device. Re-read it after the dispatch.
					currentmouse = mouse;
					if(disposed || currentmouse == null || currentmouse.Disposed)
						return new Vector2D();

					MouseState ms = currentmouse.Poll();
					msX = ms.X;
					msY = ms.Y;
				}
				else // Fallback implementation for unix
				{
					if(source == null || source.IsDisposed || !source.IsHandleCreated)
						return new Vector2D();

					Point pos = Cursor.Position;

					Rectangle clipBox = source.RectangleToScreen(source.ClientRectangle); //Cursor.Clip;
					Cursor.Position = new Point(clipBox.X + clipBox.Width / 2, clipBox.Y + clipBox.Height / 2);

					if(firstProcess)
					{
						lastPos = Cursor.Position;
						firstProcess = false;
					}

					msX = (float)(pos.X - lastPos.X);
					msY = (float)(pos.Y - lastPos.Y);
					lastPos = Cursor.Position;
				}

				// Calculate changes depending on sensitivity
				float changex = msX * General.Settings.VisualMouseSensX * General.Settings.MouseSpeed * 0.01f;
				float changey = msY * General.Settings.VisualMouseSensY * General.Settings.MouseSpeed * 0.01f;

				return new Vector2D(changex, changey);
			}
			finally
			{
				processing = false;
			}
		}

		#endregion

		#if MONO_WINFORMS
		[DllImport("BuilderNative.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern void MouseInput_ShowCursor(bool show);
		#endif
	}

	public struct MouseState
	{
		public MouseState(float x, float y) { X = x; Y = y; }
		public float X { get; }
		public float Y { get; }
	}

	public class RawMouse
	{
		public RawMouse(System.Windows.Forms.Control control)
		{
			Handle = RawMouse_New(control.Handle);
			if (Handle == IntPtr.Zero)
				throw new Exception("RawMouse_New failed");
		}

		~RawMouse()
		{
			Dispose();
		}

		public MouseState Poll()
		{
			IntPtr handle = Handle;
			if(handle == IntPtr.Zero) return new MouseState(0.0f, 0.0f);
			return new MouseState(RawMouse_GetX(handle), RawMouse_GetY(handle));
		}

		public bool Disposed { get { return Handle == IntPtr.Zero; } }

		public void Dispose()
		{
			if (!Disposed)
			{
				RawMouse_Delete(Handle);
				Handle = IntPtr.Zero;
			}
		}

		internal IntPtr Handle;

		[DllImport("BuilderNative.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern IntPtr RawMouse_New(IntPtr windowHandle);

		[DllImport("BuilderNative.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern void RawMouse_Delete(IntPtr handle);

		[DllImport("BuilderNative.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern float RawMouse_GetX(IntPtr handle);

		[DllImport("BuilderNative.dll", CallingConvention = CallingConvention.Cdecl)]
		static extern float RawMouse_GetY(IntPtr handle);
	}
}
