using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;
using GHPC;
using GHPC.Audio;
using GHPC.Camera;
using GHPC.Weapons;
using GHPC.World;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeclercSXXI.Common
{
	internal static class GunShotSound
	{
		// Fields a tank never sets keep their defaults here: another tank sets them (CS0649 is off for this declaration only).
#pragma warning disable CS0649
		internal sealed class Settings
		{
			internal string ModName;
			internal string What = "gun sound";
			internal string ExteriorResource;
			internal string InteriorResource;
			internal float StartOffsetMs;
			internal float Volume = 1f;
			internal float MinDistance = 180f;
			internal float MaxDistance = 5000f;
			internal float InteriorLevel3D = 0f;
			internal string BusPath;
			internal string FallbackEvent;
		}
#pragma warning restore CS0649

		internal sealed class Loaded
		{
			internal Sound Exterior, Interior;
			internal uint ExteriorMs, InteriorMs;
			internal FMOD.Studio.Bus Bus;
			internal bool BusLocked;
		}

		struct Playing
		{
			internal Channel Channel;
			internal float Volume;
		}

		static readonly Dictionary<string, Loaded> Cache = new Dictionary<string, Loaded>();
		static readonly HashSet<string> Warned = new HashSet<string>();
		static readonly List<Playing> Active = new List<Playing>();
		static bool _sceneHook;
		static Ticker _ticker;

		internal static bool Attach(Settings s, WeaponSystem gun, IUnit unit)
		{
			var audio = gun != null ? gun.WeaponSound : null;
			if (audio == null) return false;
			audio.SingleShotEventPaths = new[] { s.FallbackEvent };
			var loaded = Load(s);
			if (loaded == null) return false;
			audio.SingleShotEventPaths = new string[0];
			audio.LoopEventPath = "";
			gun.Fired += (a, r) => OnFired(s, loaded, gun, audio, unit);
			return true;
		}

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
				if (Create(s, s.ExteriorResource, out var ext, out var extMs) && Create(s, s.InteriorResource, out var inn, out var innMs))
				{
					result = new Loaded { Exterior = ext, ExteriorMs = extMs, Interior = inn, InteriorMs = innMs };
					LockBus(s, result);
				}
			}
			catch (Exception)
			{
				WarnOnce(s, $"its {s.What} could not be loaded");
			}
			Cache[s.ExteriorResource] = result;
			return result;
		}

		static bool Create(Settings s, string resource, out Sound sound, out uint ms)
		{
			sound = default(Sound);
			ms = 0;
			byte[] data;
			using (var stream = typeof(GunShotSound).Assembly.GetManifestResourceStream(resource))
			{
				if (stream == null)
				{
					WarnOnce(s, $"its {s.What} is missing from the mod");
					return false;
				}
				using (var memory = new MemoryStream())
				{
					stream.CopyTo(memory);
					data = memory.ToArray();
				}
			}
			if (!GunShotRules.IsPcmWav(data, out ms))
			{
				WarnOnce(s, $"its {s.What} is damaged");
				return false;
			}
			var info = new CREATESOUNDEXINFO { cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)), length = (uint)data.Length };
			var mode = MODE.OPENMEMORY | MODE.CREATESAMPLE | MODE._3D | MODE._3D_INVERSEROLLOFF | MODE.LOOP_OFF;
			var result = RuntimeManager.CoreSystem.createSound(data, mode, ref info, out sound);
			if (result != RESULT.OK)
			{
				WarnOnce(s, $"its {s.What} could not be opened");
				return false;
			}
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

		static void OnFired(Settings s, Loaded l, WeaponSystem gun, WeaponAudio audio, IUnit unit)
		{
			Vector3 position = Vector3.zero;
			try
			{
				position = gun.MuzzleIdentity.position;
				bool interior = VehicleParameterTracker.IsVehicleInterior(unit);
				var cam = CameraManager.MainCam;
				float delay = interior || cam == null ? 0f : WorldEnvironmentManager.GetSoundDelay(position, cam.transform.position);
				if (GunShotRules.ShouldDelay(delay, Time.deltaTime)) audio.StartCoroutine(Delayed(s, l, position, unit, interior, delay));
				else Play(s, l, position, unit, interior);
			}
			catch (Exception)
			{
				Fail(s, position, unit);
			}
		}

		static IEnumerator Delayed(Settings s, Loaded l, Vector3 at, IUnit unit, bool interior, float delay)
		{
			yield return new WaitForSeconds(delay);
			try { Play(s, l, at, unit, interior); }
			catch (Exception)
			{
				Fail(s, at, unit);
			}
		}

		static void Fail(Settings s, Vector3 at, IUnit unit)
		{
			WarnOnce(s, "its gun sound failed to play");
			try { VehicleParameterTracker.PlayOneShot(s.FallbackEvent, at, unit); }
			catch (Exception) { }
		}

		static void Play(Settings s, Loaded l, Vector3 at, IUnit unit, bool interior)
		{
			var sound = interior ? l.Interior : l.Exterior;
			var result = RuntimeManager.CoreSystem.playSound(sound, Group(s, l), true, out var channel);
			if (result != RESULT.OK)
			{
				Fail(s, at, unit);
				return;
			}
			var pos = RuntimeUtils.ToFMODVector(at);
			var vel = default(VECTOR);
			channel.set3DAttributes(ref pos, ref vel);
			channel.set3DMinMaxDistance(s.MinDistance, s.MaxDistance);
			channel.set3DLevel(interior ? s.InteriorLevel3D : 1f);
			channel.setVolume(GunShotRules.ChannelVolume(s.Volume, AudioListener.volume));
			uint skip = GunShotRules.ClampOffsetMs(s.StartOffsetMs, interior ? l.InteriorMs : l.ExteriorMs);
			if (skip > 0) channel.setPosition(skip, TIMEUNIT.MS);
			channel.setPaused(false);
			Active.Add(new Playing { Channel = channel, Volume = s.Volume });
			EnsureTicker();
		}

		static void EnsureTicker()
		{
			if (_ticker == null)
			{
				var ticker = new GameObject("GunShotSound ticker");
				UnityEngine.Object.DontDestroyOnLoad(ticker);
				_ticker = ticker.AddComponent<Ticker>();
			}
			if (!_sceneHook)
			{
				_sceneHook = true;
				SceneManager.sceneUnloaded += _ => StopAll();
			}
		}

		static void StopAll()
		{
			foreach (var playing in Active) playing.Channel.stop();
			Active.Clear();
		}

		sealed class Ticker : MonoBehaviour
		{
			void Update()
			{
				for (int i = Active.Count - 1; i >= 0; i--)
				{
					var playing = Active[i];
					if (playing.Channel.isPlaying(out bool stillPlaying) != RESULT.OK || !stillPlaying)
					{
						Active.RemoveAt(i);
						continue;
					}
					playing.Channel.setVolume(GunShotRules.ChannelVolume(playing.Volume, AudioListener.volume));
				}
			}
		}
	}
}
