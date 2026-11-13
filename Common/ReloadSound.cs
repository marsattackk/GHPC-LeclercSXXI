using System;
using System.Collections.Generic;
using System.Reflection;
using FMOD;
using FMODUnity;
using GHPC;
using GHPC.Audio;
using GHPC.Camera;
using GHPC.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeclercSXXI.Common
{
	internal static class ReloadSound
	{
		internal sealed class Settings
		{
			internal GunShotSound.Settings Sound;
			internal float ClipSeconds;
			internal float[] HoldPoints;
			internal float MaxHearDistance = 250f;
			internal float SyncTolerance = 0.15f;
			internal Settings[] Variants;
			internal MODE Rolloff = MODE._3D_INVERSEROLLOFF;
			internal Func<AmmoFeed, Vector3> Position;
		}

		sealed class Run
		{
			internal Settings S;
			internal GunShotSound.Loaded L;
			internal Settings[] Variants;
			internal GunShotSound.Loaded[] Sounds;
			internal float[] Lengths;
			internal AmmoFeed Feed;
			internal WeaponSystem Gun;
			internal IUnit Unit;
			internal Channel Channel;
			internal bool HasChannel, Interior, Near = true;
			internal float LastClock = -1f, NextRangeCheck, NextStart;
		}

		static readonly FieldInfo Clock = Reflect.Field(typeof(AmmoFeed), "_clipFeedTime");
		static readonly FieldInfo WholeEvent = Reflect.Field(typeof(AmmoFeed), "_clipReloadFMODEvent");
		static readonly List<Run> Active = new List<Run>();
		static bool _sceneHook;
		static Ticker _ticker;

		internal static void Attach(Settings s, WeaponSystem gun, IUnit unit)
		{
			var feed = gun != null ? gun.Feed : null;
			if (feed == null || feed.ClipReloadStages == null) return;
			if (Clock == null || WholeEvent == null) { GunShotSound.WarnOnce(s.Sound, "its reload sound does not fit this version of the game"); return; }
			var variants = s.Variants ?? new[] { s };
			var loaded = new GunShotSound.Loaded[variants.Length];
			var lengths = new float[variants.Length];
			for (int i = 0; i < variants.Length; i++)
			{
				loaded[i] = GunShotSound.Load(variants[i].Sound);
				if (loaded[i] == null) return;
				lengths[i] = variants[i].ClipSeconds;
			}
			WholeEvent.SetValue(feed, "");
			foreach (var stage in feed.ClipReloadStages)
			{
				stage.StageAudio = null;
				stage.StageClips = null;
				stage.StageAudioEvent = null;
				stage.StageEventReference = default(EventReference);
			}
			feed.ReloadStageStarted += stage => {
				if (stage != 0) return;
				Begin(variants, loaded, lengths, feed, gun, unit);
			};
		}

		static void Begin(Settings[] variants, GunShotSound.Loaded[] sounds, float[] lengths, AmmoFeed feed, WeaponSystem gun, IUnit unit)
		{
			foreach (var r in Active) if (r.Feed == feed) return;
			Active.Add(new Run { Variants = variants, Sounds = sounds, Lengths = lengths, Feed = feed, Gun = gun, Unit = unit });
			if (_ticker == null)
			{
				var go = new GameObject("ReloadSound ticker");
				UnityEngine.Object.DontDestroyOnLoad(go);
				_ticker = go.AddComponent<Ticker>();
			}
			if (!_sceneHook) { _sceneHook = true; SceneManager.sceneUnloaded += _ => StopAll(); }
		}

		static void Stop(Run r)
		{
			if (r.HasChannel) r.Channel.stop();
			r.HasChannel = false;
		}

		static void StopAll()
		{
			foreach (var r in Active) Stop(r);
			Active.Clear();
		}

		static void Start(Run r, float at, Vector3 pos)
		{
			var sound = r.Interior ? r.L.Interior : r.L.Exterior;
			var result = RuntimeManager.CoreSystem.playSound(sound, GunShotSound.Group(r.S.Sound, r.L), true, out r.Channel);
			if (result != RESULT.OK) return;
			r.HasChannel = true;
			r.Channel.setPriority(VehicleParameterTracker.IsPlayerUnit(r.Unit) ? 32 : 128);
			r.Channel.setMode(r.Interior && r.S.Sound.InteriorLevel3D == 0f ? MODE._2D : MODE._3D | r.S.Rolloff);
			r.Channel.set3DMinMaxDistance(r.S.Sound.MinDistance, r.S.Sound.MaxDistance);
			r.Channel.set3DLevel(r.Interior ? r.S.Sound.InteriorLevel3D : 1f);
			r.Channel.setPosition((uint)(at * 1000f), TIMEUNIT.MS);
			Place(r, pos);
			r.Channel.setVolume(GunShotRules.ChannelVolume(r.S.Sound.Volume, AudioListener.volume));
			r.Channel.setPaused(false);
		}

		static void Place(Run r, Vector3 at)
		{
			var pos = RuntimeUtils.ToFMODVector(at);
			var vel = default(VECTOR);
			r.Channel.set3DAttributes(ref pos, ref vel);
		}

		static bool Tick(Run r)
		{
			if (r.Feed == null || r.Gun == null || !r.Feed.Reloading || r.Unit.Destroyed || r.Unit.Neutralized) return false;
			if (r.S == null) {
				int i = ReloadRules.CycleIndex(r.Feed.TotalReloadTime, r.Lengths);
				r.S = r.Variants[i]; r.L = r.Sounds[i];
			}
			float clock = (float)Clock.GetValue(r.Feed);
			bool stalled = clock == r.LastClock;
			r.LastClock = clock;
			var muzzle = r.S.Position != null ? r.S.Position(r.Feed) : r.Gun.MuzzleIdentity.position;
			bool own = VehicleParameterTracker.IsPlayerUnit(r.Unit);
			var cam = CameraManager.MainCam;
			if (Time.time >= r.NextRangeCheck)
			{
				r.NextRangeCheck = Time.time + 0.25f;
				r.Near = cam != null && ReloadRules.Audible(own, Vector3.Distance(cam.transform.position, muzzle), r.S.MaxHearDistance);
			}
			if (!r.Near) { Stop(r); return true; }
			float target = ReloadRules.ClipTarget(clock, r.Feed.TotalReloadTime, r.S.ClipSeconds, r.S.HoldPoints, out bool holding);
			bool interior = VehicleParameterTracker.IsVehicleInterior(r.Unit);
			if (interior != r.Interior) { Stop(r); r.Interior = interior; }
			if (r.HasChannel && (r.Channel.isPlaying(out bool on) != RESULT.OK || !on)) r.HasChannel = false;
			if (!r.HasChannel)
			{
				if (target < r.S.ClipSeconds - 0.2f && Time.time >= r.NextStart)
				{
					r.NextStart = Time.time + 0.5f;
					Start(r, target, muzzle);
				}
				return true;
			}
			r.Channel.getPosition(out uint ms, TIMEUNIT.MS);
			var act = ReloadRules.Decide(target, holding, ms / 1000f, r.S.SyncTolerance);
			if (act == ReloadRules.Act.Seek) r.Channel.setPosition((uint)(target * 1000f), TIMEUNIT.MS);
			r.Channel.setPaused(stalled || act == ReloadRules.Act.Hold);
			Place(r, muzzle);
			r.Channel.setVolume(GunShotRules.ChannelVolume(r.S.Sound.Volume, AudioListener.volume));
			return true;
		}

		sealed class Ticker : MonoBehaviour
		{
			void Update()
			{
				for (int i = Active.Count - 1; i >= 0; i--)
				{
					var r = Active[i];
					bool go;
					try { go = Tick(r); }
					catch (Exception)
					{
						GunShotSound.WarnOnce(r.S.Sound, "its reload sound stopped working");
						go = false;
					}
					if (!go) { Stop(r); Active.RemoveAt(i); }
				}
			}
		}
	}
}
