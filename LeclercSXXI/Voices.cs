using System.Reflection;
using GHPC.Crew;
using GHPC.Effects.Voices;
using GHPC.Vehicle;
using HarmonyLib;
using LeclercSXXI.Common;

namespace LeclercSXXI
{
	internal static class Voices
	{
		const string HandlerName = "US Tank Voice (Leclerc SXXI)";
		static readonly FieldInfo Protocol = Reflect.Field(typeof(CrewVoiceHandler), "_voiceProtocolLineData");
		static readonly FieldInfo ReloadType = Reflect.Field(typeof(CrewVoiceHandler), "_reloadType");
		static readonly FieldInfo AssignedAmmo = Reflect.Field(typeof(CrewVoiceHandler), "_assignedAmmoPrefs");
		static readonly FieldInfo Routine = Reflect.Field(typeof(CrewVoiceHandler), "_voiceRoutine");
		static readonly MethodInfo SelectProtocol = Reflect.Method(typeof(CrewVoiceHandler), "SetupComponentsForProtocol");
		static readonly MethodInfo SelectActors = Reflect.Method(typeof(CrewVoiceHandler), "SelectVoiceActors");
		static readonly MethodInfo SelectAmmo = Reflect.Method(typeof(CrewVoiceHandler), "SetupAmmoPrefs");
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

		internal static void Apply(Vehicle vehicle)
		{
			WarmDonor();
			var voice = vehicle.CrewVoiceHandler;
			if (voice == null || _american == null || Protocol == null || ReloadType == null || AssignedAmmo == null
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

		[HarmonyPatch(typeof(CrewVoiceHandler), nameof(CrewVoiceHandler.PlayVoiceLine))]
		static class LoaderCallout
		{
			static bool Prefix(CrewVoiceHandler __instance, CrewPosition position) =>
				position != CrewPosition.Loader || __instance.name != HandlerName;
		}
	}
}
