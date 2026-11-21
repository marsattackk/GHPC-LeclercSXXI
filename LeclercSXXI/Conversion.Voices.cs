using GHPC.Crew;
using GHPC.Effects.Voices;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using HarmonyLib;

namespace LeclercSXXI
{
	internal static class ConversionVoices
	{
		const string HandlerName = "US Tank Voice (Leclerc SXXI)";
		static readonly System.Reflection.FieldInfo Protocol = Reflect.Field(typeof(CrewVoiceHandler), "_voiceProtocolLineData");
		static readonly System.Reflection.FieldInfo ReloadType = Reflect.Field(typeof(CrewVoiceHandler), "_reloadType");
		static readonly System.Reflection.FieldInfo AssignedAmmo = Reflect.Field(typeof(CrewVoiceHandler), "_assignedAmmoPrefs");
		static readonly System.Reflection.FieldInfo Routine = Reflect.Field(typeof(CrewVoiceHandler), "_voiceRoutine");
		static readonly System.Reflection.MethodInfo SelectProtocol = Reflect.Method(
			typeof(CrewVoiceHandler),
			"SetupComponentsForProtocol");
		static readonly System.Reflection.MethodInfo SelectActors = Reflect.Method(typeof(CrewVoiceHandler), "SelectVoiceActors");
		static readonly System.Reflection.MethodInfo SelectAmmo = Reflect.Method(typeof(CrewVoiceHandler), "SetupAmmoPrefs");
		static VoiceProtocolDataScriptable _american;
		static object _ammoPreferences;

		internal static void WarmDonor()
		{
			if (_american != null) return;
			var donor = DonorParts.Prefab("M1IP");
			var voice = donor != null ? donor.GetComponentInChildren<CrewVoiceHandler>(true) : null;
			if (voice == null) return;
			_ammoPreferences = AssignedAmmo?.GetValue(voice);
			_american = voice.VoiceProtocolLineData;
		}

		// Keep the three positions and event subscriptions of the existing crew handler. Spawns after GameReady
		// have already selected Soviet actors, so reselect the protocol, actors and ammo preferences in that case.
		internal static void Apply(Vehicle vehicle)
		{
			WarmDonor();
			if (_american == null)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName} voices: no M1IP crew voice to copy; the crew keeps its Russian voices.");
				return;
			}
			var voice = vehicle.CrewVoiceHandler;
			if (voice == null || Protocol == null || ReloadType == null || AssignedAmmo == null
				|| Routine == null || SelectProtocol == null || SelectActors == null || SelectAmmo == null) return;
			bool initialized = Routine.GetValue(voice) != null;
			bool awake = voice.IsInitialized && voice.IsCurrentUnit && voice.isActiveAndEnabled;
			if (awake) voice.GoToSleep();
			Protocol.SetValue(voice, _american);
			AssignedAmmo.SetValue(voice, _ammoPreferences);
			ReloadType.SetValue(voice, CrewVoiceHandler.ReloaderType.Autoloader);
			voice.name = HandlerName;
			if (initialized)
			{
				SelectAmmo.Invoke(voice, null);
				SelectProtocol.Invoke(voice, null);
				SelectActors.Invoke(voice, null);
			}
			if (awake) voice.WakeUp();
		}

		// The American routines request loader callouts even on crews without a loader.
		[HarmonyPatch(typeof(CrewVoiceHandler), nameof(CrewVoiceHandler.PlayVoiceLine))]
		static class LoaderCallout
		{
			static bool Prefix(CrewVoiceHandler __instance, CrewPosition position) =>
				position != CrewPosition.Loader || __instance.name != HandlerName;
		}
	}
}
