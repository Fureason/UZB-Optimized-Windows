#region === Copyright (c) 2026 Ultimate Zone Builder contributors ===

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace CodeImp.DoomBuilder
{
	// Discord's local IPC connection runs entirely off the UI thread. Discord is
	// optional: a missing/closed client or an unset application id has no effect
	// on editing, startup, or shutdown.
	internal sealed class DiscordRichPresence : IDisposable
	{
		private sealed class Activity
		{
			public string Details;
			public string State;
			public long Started;
			public string Signature;
		}

		private readonly string applicationid;
		private readonly string largeimage;
		private readonly AutoResetEvent changed = new AutoResetEvent(false);
		private readonly object synclock = new object();
		private readonly Thread worker;
		private readonly long sessionstarted;
		private volatile bool stopping;
		private Activity pending;

		public static DiscordRichPresence CreateFromSettings()
		{
			if(General.Settings == null || !General.Settings.ReadSetting("discord.enabled", true)) return null;

			string applicationid = General.Settings.ReadSetting("discord.applicationid", string.Empty).Trim();
			ulong ignored;
			if(string.IsNullOrEmpty(applicationid) || !ulong.TryParse(applicationid, out ignored)) return null;

			string largeimage = General.Settings.ReadSetting("discord.largeimage", string.Empty).Trim();
			return new DiscordRichPresence(applicationid, largeimage);
		}

		private DiscordRichPresence(string applicationid, string largeimage)
		{
			this.applicationid = applicationid;
			this.largeimage = largeimage;
			sessionstarted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Discord Rich Presence" };
			worker.Start();
		}

		public void UpdateActivity()
		{
			string details = "In the editor";
			string state = "No map open";

			if(General.Map != null)
			{
				string mapname = (General.Map.Options != null ? General.Map.Options.CurrentName : string.Empty);
				string filename = Path.GetFileName(General.Map.FileTitle ?? string.Empty);
				string mode = (General.Editing != null && General.Editing.Mode != null)
					? General.Editing.Mode.Attributes.DisplayName : "Map editor";

				details = !string.IsNullOrEmpty(mapname) ? "Editing " + mapname : "Editing a map";
				state = !string.IsNullOrEmpty(filename) ? mode + " | " + filename : mode;
			}

			Activity next = new Activity
			{
				Details = details,
				State = state,
				Started = sessionstarted,
				Signature = details + "\n" + state + "\n" + sessionstarted
			};

			lock(synclock)
			{
				if(pending != null && pending.Signature == next.Signature) return;
				pending = next;
			}
			changed.Set();
		}

		private void WorkerLoop()
		{
			NamedPipeClientStream pipe = null;
			string sent = string.Empty;
			while(!stopping)
			{
				changed.WaitOne(pipe == null ? 5000 : 30000);
				if(stopping) break;

				Activity current;
				lock(synclock) current = pending;
				if(current == null || current.Signature == sent) continue;

				try
				{
					if(pipe == null || !pipe.IsConnected)
					{
						if(pipe != null) pipe.Dispose();
						pipe = Connect();
						if(pipe == null) continue;
					}

					WriteFrame(pipe, 1, BuildActivityJson(current));
					ReadFrame(pipe, 1500); // Consume Discord's acknowledgement.
					sent = current.Signature;
				}
				catch
				{
					if(pipe != null) pipe.Dispose();
					pipe = null;
					sent = string.Empty;
				}
			}

			if(pipe != null) pipe.Dispose();
		}

		private NamedPipeClientStream Connect()
		{
			for(int i = 0; i < 10 && !stopping; i++)
			{
				NamedPipeClientStream candidate = null;
				try
				{
					candidate = new NamedPipeClientStream(".", "discord-ipc-" + i,
						PipeDirection.InOut, PipeOptions.Asynchronous);
					candidate.Connect(100);
					WriteFrame(candidate, 0, "{\"v\":1,\"client_id\":\"" + Escape(applicationid) + "\"}");
					ReadFrame(candidate, 1500); // READY
					return candidate;
				}
				catch
				{
					if(candidate != null) candidate.Dispose();
				}
			}
			return null;
		}

		private string BuildActivityJson(Activity activity)
		{
			StringBuilder json = new StringBuilder(512);
			json.Append("{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":")
				.Append(Process.GetCurrentProcess().Id)
				.Append(",\"activity\":{\"details\":\"").Append(Escape(activity.Details))
				.Append("\",\"state\":\"").Append(Escape(activity.State))
				.Append("\",\"timestamps\":{\"start\":").Append(activity.Started).Append('}');
			if(!string.IsNullOrEmpty(largeimage))
				json.Append(",\"assets\":{\"large_image\":\"").Append(Escape(largeimage))
					.Append("\",\"large_text\":\"Ultimate Zone Builder\"}");
			json.Append(",\"instance\":false}},\"nonce\":\"")
				.Append(Guid.NewGuid().ToString("N")).Append("\"}");
			return json.ToString();
		}

		private static string Escape(string value)
		{
			if(string.IsNullOrEmpty(value)) return string.Empty;
			return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
				.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
		}

		private static void WriteFrame(Stream stream, int opcode, string json)
		{
			byte[] payload = Encoding.UTF8.GetBytes(json);
			byte[] header = new byte[8];
			Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, header, 0, 4);
			Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);
			stream.Write(header, 0, header.Length);
			stream.Write(payload, 0, payload.Length);
			stream.Flush();
		}

		private static void ReadFrame(Stream stream, int timeout)
		{
			byte[] header = new byte[8];
			ReadExact(stream, header, timeout);
			int length = BitConverter.ToInt32(header, 4);
			if(length < 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid Discord IPC frame.");
			if(length > 0) ReadExact(stream, new byte[length], timeout);
		}

		private static void ReadExact(Stream stream, byte[] buffer, int timeout)
		{
			int offset = 0;
			while(offset < buffer.Length)
			{
				IAsyncResult result = stream.BeginRead(buffer, offset, buffer.Length - offset, null, null);
				if(!result.AsyncWaitHandle.WaitOne(timeout)) throw new TimeoutException();
				int read = stream.EndRead(result);
				if(read <= 0) throw new EndOfStreamException();
				offset += read;
			}
		}

		public void Dispose()
		{
			stopping = true;
			changed.Set();
			if(worker != null && worker.IsAlive) worker.Join(500);
			changed.Dispose();
		}
	}
}

#endregion
