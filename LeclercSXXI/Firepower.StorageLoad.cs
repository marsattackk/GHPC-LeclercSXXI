using System;
using GHPC.Audio;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// A reload that starts while the conveyor holds none of the queued round came from outside it (another mod's load from the
	// hull racks, e.g. No Restock Reload). The conveyor does not move for it, so the autoloader's sound is stopped and a loader's
	// hand load plays instead: GHPC's own Abrams loader sounds (my pick), their start times stretched to
	// the reload's time, on the reload's own clock (a pause holds the next sound back).
	internal sealed class StorageLoadSound : MonoBehaviour
	{
		// GHPC's M1IP main gun feed: stage events and seconds.
		static readonly string[] Events = {
			"event:/Vehicles/M1_Abrams/Elements/Element0",
			"event:/Vehicles/M1_Abrams/Elements/Element1",
			"event:/Vehicles/M1_Abrams/Elements/Element2",
		};
		// The M1IP feed's own stage seconds in the game (6 s together); the cues are stretched to the Leclerc's reload time.
		static readonly float[] StageSeconds = { 0.5f, 3.25f, 2.25f };

		static readonly System.Reflection.FieldInfo Clock = Reflect.Field(typeof(AmmoFeed), "_clipFeedTime");
		static readonly System.Reflection.FieldInfo LockedIn = Reflect.Field(typeof(AmmoFeed), "_queuedClipTypeLockedIn");
		static readonly System.Reflection.FieldInfo AutoloaderEvent = Reflect.Field(typeof(AmmoFeed), "_clipReloadFMODEventInstance");

		AmmoFeed _feed;
		internal void Watch(AmmoFeed feed) => _feed = feed;
		readonly FMOD.Studio.EventInstance[] _playing = new FMOD.Studio.EventInstance[Events.Length];
		int _next = -1;   // -1: no storage load running

		void Begin()
		{
			if (AutoloaderEvent != null)
			{
				var autoloader = (FMOD.Studio.EventInstance)AutoloaderEvent.GetValue(_feed);
				FmodGenericAudioManager.StopEvent(ref autoloader);
				AutoloaderEvent.SetValue(_feed, autoloader);
			}
			ReloadSound.Silence(_feed);   // the Leclerc's own conveyor sound (AutoloaderSound), started by this same FeedNewClip
			Stop();
			_next = 0;
		}

		void Update()
		{
			if (_next < 0 || _feed == null) return;
			if (!_feed.Reloading)
			{
				// A finished load lets its last sound ring out; a cancelled one (no round in the breech) stops.
				if (_feed.AmmoTypeInBreech == null) Stop();
				_next = -1;
				return;
			}
			if (_next >= Events.Length) return;
			// Read every frame: the total changes when a loader is lost mid-load.
			var cues = ReloadRules.CueTimes(StageSeconds, _feed.TotalReloadTime);
			float clock = (float)Clock.GetValue(_feed);
			while (_next < Events.Length && clock >= cues[_next])
			{
				try { _playing[_next] = FmodGenericAudioManager.StartEvent(Events[_next], _feed.gameObject, null, false); }
				catch (Exception exception)
				{
					PatchGuard.Failed("Leclerc storage load sound", exception);
					_next = Events.Length;
					return;
				}
				_next++;
			}
		}

		void Stop()
		{
			for (int index = 0; index < _playing.Length; index++) FmodGenericAudioManager.StopEvent(ref _playing[index]);
		}

		void OnDisable()
		{
			Stop();
			_next = -1;
		}

		// After every other FeedNewClip patch (per-type timing included), whoever started the reload.
		[HarmonyPatch(typeof(AmmoFeed), nameof(AmmoFeed.FeedNewClip))]
		static class Detect
		{
			// Read before any other mod's prefix can start the reload.
			[HarmonyPriority(Priority.First)]
			static void Prefix(AmmoFeed __instance, out bool __state) => __state = __instance.Reloading;

			[HarmonyPriority(Priority.Last)]
			static void Postfix(AmmoFeed __instance, bool __state)
			{
				try
				{
					// Only a reload started by this very call.
					if (__state || !__instance.Reloading || Clock == null || LockedIn == null) return;
					if (__instance.GetComponent<ConveyorLoader>() == null) return;
					var queued = LockedIn.GetValue(__instance) as AmmoType.AmmoClip;
					if (queued == null || __instance.ReadyRack == null || __instance.ReadyRack.HasClipWithPattern(queued)) return;
					// Fitted by ConveyorLoader.Attach.
					var sound = __instance.GetComponent<StorageLoadSound>() ?? __instance.gameObject.AddComponent<StorageLoadSound>();
					sound.Watch(__instance);
					sound.Begin();
				}
				catch (Exception exception) { PatchGuard.Failed("Leclerc storage load sound", exception); }
			}
		}
	}
}
