using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using MelonLoader;

namespace LeclercSXXI.Common
{
	internal static class GunShotSound
	{
		internal sealed class Settings
		{
			internal string ModName;
			internal string What = "gun sound";
			internal string ExteriorResource;
			internal string InteriorResource;
			internal float Volume = 1f;
			internal float MinDistance = 180f;
			internal float MaxDistance = 5000f;
			internal float InteriorLevel3D = 0f;
			internal string BusPath;
		}

		internal sealed class Loaded
		{
			internal Sound Exterior, Interior;
			internal FMOD.Studio.Bus Bus;
			internal bool BusLocked;
		}

		static readonly Dictionary<string, Loaded> Cache = new Dictionary<string, Loaded>();
		static readonly HashSet<string> Warned = new HashSet<string>();

		internal static void WarnOnce(Settings s, string what)
		{
			if (Warned.Add(s.ModName + what)) MelonLogger.Warning($"{s.ModName}: {what}; the game's own sound is used.");
		}

		internal static Loaded Load(Settings s)
		{
			if (Cache.TryGetValue(s.ExteriorResource, out var cached)) return cached;
			Loaded result = null;
			try
			{
				if (Create(s, s.ExteriorResource, out var ext) && Create(s, s.InteriorResource, out var inn))
				{
					result = new Loaded { Exterior = ext, Interior = inn};
					LockBus(s, result);
				}
			}
			catch (Exception) { WarnOnce(s, $"its {s.What} could not be loaded"); }
			Cache[s.ExteriorResource] = result;
			return result;
		}

		static bool Create(Settings s, string resource, out Sound sound)
		{
			uint ms = default;
			sound = default(Sound);
			ms = 0;
			byte[] data;
			using (var st = typeof(GunShotSound).Assembly.GetManifestResourceStream(resource))
			{
				if (st == null) { WarnOnce(s, $"its {s.What} is missing from the mod"); return false; }
				using (var m = new MemoryStream()) { st.CopyTo(m); data = m.ToArray(); }
			}
			if (!GunShotRules.IsPcmWav(data, out ms)) { WarnOnce(s, $"its {s.What} is damaged"); return false; }
			var info = new CREATESOUNDEXINFO { cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)), length = (uint)data.Length };
			var mode = MODE.OPENMEMORY | MODE.CREATESAMPLE | MODE._3D | MODE._3D_INVERSEROLLOFF | MODE.LOOP_OFF;
			var r = RuntimeManager.CoreSystem.createSound(data, mode, ref info, out sound);
			if (r != RESULT.OK) { WarnOnce(s, $"its {s.What} could not be opened"); return false; }
			return true;
		}

		static void LockBus(Settings s, Loaded l)
		{
			if (string.IsNullOrEmpty(s.BusPath)) return;
			if (RuntimeManager.StudioSystem.getBus(s.BusPath, out l.Bus) != RESULT.OK) return;
			if (l.Bus.lockChannelGroup() == RESULT.OK) l.BusLocked = RuntimeManager.StudioSystem.flushCommands() == RESULT.OK;
		}

		internal static ChannelGroup Group(Settings s, Loaded l)
		{
			if (l.BusLocked && l.Bus.isValid() && l.Bus.getChannelGroup(out var group) == RESULT.OK) return group;
			if (!l.BusLocked || !l.Bus.isValid()) LockBus(s, l);
			return default(ChannelGroup);
		}
	}
}
