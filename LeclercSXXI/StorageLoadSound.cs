using System;
using System.Reflection;
using FMOD.Studio;
using GHPC.Audio;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	internal sealed class StorageLoadSound : MonoBehaviour
	{
		static readonly string[] Events = {
			"event:/Vehicles/M1_Abrams/Elements/Element0",
			"event:/Vehicles/M1_Abrams/Elements/Element1",
			"event:/Vehicles/M1_Abrams/Elements/Element2",
		};
		static readonly float[] StageSeconds = { 0.5f, 3.25f, 2.25f };

		static readonly FieldInfo Clock = Reflect.Field(typeof(AmmoFeed), "_clipFeedTime");
		static readonly FieldInfo LockedIn = Reflect.Field(typeof(AmmoFeed), "_queuedClipTypeLockedIn");
		static readonly FieldInfo AutoloaderEvent = Reflect.Field(typeof(AmmoFeed), "_clipReloadFMODEventInstance");

		AmmoFeed _feed;
		internal void Watch(AmmoFeed feed) => _feed = feed;
		readonly EventInstance[] _playing = new EventInstance[Events.Length];
		int _next = -1;

		void Begin()
		{
			if (AutoloaderEvent != null)
			{
				var e = (EventInstance)AutoloaderEvent.GetValue(_feed);
				FmodGenericAudioManager.StopEvent(ref e);
				AutoloaderEvent.SetValue(_feed, e);
			}
			ReloadSound.Silence(_feed);
			Stop();
			_next = 0;
		}

		void Update()
		{
			if (_next < 0 || _feed == null) return;
			if (!_feed.Reloading)
			{
				if (_feed.AmmoTypeInBreech == null) Stop();
				_next = -1;
				return;
			}
			if (_next >= Events.Length) return;
			var cues = ReloadRules.CueTimes(StageSeconds, _feed.TotalReloadTime);
			float clock = (float)Clock.GetValue(_feed);
			while (_next < Events.Length && clock >= cues[_next])
			{
				try { _playing[_next] = FmodGenericAudioManager.StartEvent(Events[_next], _feed.gameObject, null, false); }
				catch (Exception e) { PatchGuard.Failed("Leclerc storage load sound", e); _next = Events.Length; return; }
				_next++;
			}
		}

		void Stop()
		{
			for (int i = 0; i < _playing.Length; i++) FmodGenericAudioManager.StopEvent(ref _playing[i]);
		}

		void OnDisable() { Stop(); _next = -1; }

		[HarmonyPatch(typeof(AmmoFeed), nameof(AmmoFeed.FeedNewClip))]
		static class Detect
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(AmmoFeed __instance, out bool __state) => __state = __instance.Reloading;

			[HarmonyPriority(Priority.Last)]
			static void Postfix(AmmoFeed __instance, bool __state)
			{
				try
				{
					if (__state || !__instance.Reloading || Clock == null || LockedIn == null) return;
					if (__instance.GetComponent<ConveyorLoader>() == null) return;
					var queued = LockedIn.GetValue(__instance) as AmmoType.AmmoClip;
					if (queued == null || __instance.ReadyRack == null || __instance.ReadyRack.HasClipWithPattern(queued)) return;
					var sound = __instance.GetComponent<StorageLoadSound>() ?? __instance.gameObject.AddComponent<StorageLoadSound>();
					sound._feed = __instance;
					sound.Begin();
				}
				catch (Exception e) { PatchGuard.Failed("Leclerc storage load sound", e); }
			}
		}
	}
}
