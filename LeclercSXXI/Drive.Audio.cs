using FMOD;
using FMOD.Studio;
using GHPC.Audio;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// GHPC selects the engine and track recordings by the FMOD Vehicle parameter.
	// The native diesel event (Leopard 2A3) decides how loud the engine is and where. The TM 307B turbine of the Hyperbar is
	// a second engine event: the game's T-80 gas turbine, high-passed so only its whine is left (its own rumble would fight
	// the diesel), laid about 20 dB under the diesel. Chosen by ear from offline mock-ups. The turbine's sound has no
	// written source, so the layer is inferred.
	internal sealed partial class EngineAudio : MonoBehaviour
	{
		// Volume that puts the high-passed whine about 20 dB under the diesel. The renders gave 0.9 outside / 1.8 inside
		// for 14 dB under (outside 1.00 at idle and 0.84 flat out, inside 2.00 / 1.68). In game that was too loud, inside and
		// outside, so it is half (-6 dB). FMOD volume above 1 amplifies.
		const float TurbineGainOutside = 0.45f;
		const float TurbineGainInside = 0.9f;
		const float WhineHighPassHz = 1200f;
		// The TM 307B keeps spinning at idle (it runs alone even with the diesel off): its RPM parameter starts at
		// 0.5 with the diesel idling and reaches 0.9 at the diesel's limiter (the two points the mock-up was rendered at).
		const float TurbineRpmIdle = 0.5f, TurbineRpmSpan = 0.4f;

		static readonly HarmonyLib.AccessTools.FieldRef<VehicleAudioController, AudibleEventInstance> Engine =
			Reflect.FieldRefAccess<VehicleAudioController, AudibleEventInstance>("_engineInstance");
		static readonly HarmonyLib.AccessTools.FieldRef<VehicleAudioController, FmodParameters.VehicleLabel> Label =
			Reflect.FieldRefAccess<VehicleAudioController, FmodParameters.VehicleLabel>("_vehicleParameter");
		static readonly PARAMETER_ID[] TurbineParameters =
		{
			FmodParameters.RPM,
			FmodParameters.Load,
			FmodParameters.Throttle,
			FmodParameters.Gear
		};

		Vehicle _vehicle;
		VehicleAudioController _audio;
		EventInstance _turbine;
		DSP _highPass;
		ChannelGroup _highPassGroup;
		readonly float[] _values = new float[TurbineParameters.Length];

		internal static void Apply(Vehicle vehicle)
		{
			var audio = vehicle.GetComponent<VehicleAudioController>();
			if (audio == null || Label == null)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: could not set the Leopard engine and track sounds.");
				return;
			}
			Label(audio) = FmodParameters.VehicleLabel.LEO2;
			if (Engine == null) return;
			vehicle.gameObject.AddComponent<EngineAudio>().Init(vehicle, audio);
		}

		void Init(Vehicle vehicle, VehicleAudioController audio)
		{
			_vehicle = vehicle;
			_audio = audio;
		}

		void LateUpdate()
		{
			var diesel = Engine(_audio)?.eventInstance ?? default;
			if (!diesel.isValid() || diesel.get3DAttributes(out var position) != RESULT.OK)
			{
				StopTurbine();
				return;
			}
			for (int index = 0; index < TurbineParameters.Length; index++)
				diesel.getParameterByID(TurbineParameters[index], out _values[index], out _);
			_values[0] = TurbineRpmIdle + TurbineRpmSpan * Mathf.Clamp01(_values[0]);

			if (!_turbine.isValid())
			{
				_turbine = VehicleParameterTracker.StartEvent(VehicleAudioManager.EngineEvent, null, null, _vehicle, true,
					(FmodParameters.Vehicle, (float)FmodParameters.VehicleLabel.T80),
					(FmodParameters.RPM, _values[0]), (FmodParameters.Load, _values[1]),
					(FmodParameters.Throttle, _values[2]), (FmodParameters.Gear, _values[3]));
				if (!_turbine.isValid()) return;
			}
			bool outside = GHPC.Camera.CameraSlot.CurrentSlotIsExterior;
			// The event's channel group exists only once FMOD has started it: until the filter is on, the whine stays
			// silent, never the full T-80 sound with its rumble.
			if (!_highPass.hasHandle()) AttachHighPass();
			_turbine.setVolume(!_highPass.hasHandle() ? 0f : outside ? TurbineGainOutside : TurbineGainInside);
			_turbine.set3DAttributes(position);
			_turbine.setParametersByIDs(TurbineParameters, _values, _values.Length);
			DevLateUpdate(diesel, outside);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		partial void DevLateUpdate(EventInstance diesel, bool outside);

		void AttachHighPass()
		{
			if (_turbine.getChannelGroup(out var group) != RESULT.OK || !group.hasHandle()) return;
			var core = FMODUnity.RuntimeManager.CoreSystem;
			if (core.createDSPByType(DSP_TYPE.MULTIBAND_EQ, out var equaliser) != RESULT.OK) return;
			equaliser.setParameterInt((int)DSP_MULTIBAND_EQ.A_FILTER, (int)DSP_MULTIBAND_EQ_FILTER_TYPE.HIGHPASS_24DB);
			equaliser.setParameterFloat((int)DSP_MULTIBAND_EQ.A_FREQUENCY, WhineHighPassHz);
			if (group.addDSP(0, equaliser) != RESULT.OK)
			{
				equaliser.release();
				return;
			}
			_highPass = equaliser;
			_highPassGroup = group;
		}

		void OnDisable() => StopTurbine();

		void StopTurbine()
		{
			if (_highPass.hasHandle())
			{
				if (_highPassGroup.hasHandle()) _highPassGroup.removeDSP(_highPass);
				_highPass.release();
				_highPass.clearHandle();
				_highPassGroup.clearHandle();
			}
			if (_turbine.isValid()) VehicleParameterTracker.StopEvent(ref _turbine);
		}
	}
}
