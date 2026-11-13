using FMOD;
using FMOD.Studio;
using GHPC.Audio;
using GHPC.Vehicle;
using HarmonyLib;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	internal sealed class EngineAudio : MonoBehaviour
	{
		const float TurbineGainOutside = 0.45f;
		const float TurbineGainInside = 0.9f;
		const float WhineHighPassHz = 1200f;
		const float TurbineRpmIdle = 0.5f, TurbineRpmSpan = 0.4f;

		static readonly AccessTools.FieldRef<VehicleAudioController, AudibleEventInstance> Engine =
			Reflect.FieldRefAccess<VehicleAudioController, AudibleEventInstance>("_engineInstance");
		static readonly AccessTools.FieldRef<VehicleAudioController, FmodParameters.VehicleLabel> Label =
			Reflect.FieldRefAccess<VehicleAudioController, FmodParameters.VehicleLabel>("_vehicleParameter");
		static readonly PARAMETER_ID[] TurbineParameters = { FmodParameters.RPM, FmodParameters.Load, FmodParameters.Throttle, FmodParameters.Gear };

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
				MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName}: could not set the Leopard engine and track sounds.");
				return;
			}
			Label(audio) = FmodParameters.VehicleLabel.LEO2;
			if (Engine == null) return;
			var layer = vehicle.gameObject.AddComponent<EngineAudio>();
			layer._vehicle = vehicle;
			layer._audio = audio;
		}

		void LateUpdate()
		{
			var diesel = Engine(_audio)?.eventInstance ?? default;
			if (!diesel.isValid() || diesel.get3DAttributes(out var position) != RESULT.OK)
			{
				StopTurbine();
				return;
			}
			for (int i = 0; i < TurbineParameters.Length; i++)
				diesel.getParameterByID(TurbineParameters[i], out _values[i], out _);
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
			if (!_highPass.hasHandle()) AttachHighPass();
			_turbine.setVolume(!_highPass.hasHandle() ? 0f : outside ? TurbineGainOutside : TurbineGainInside);
			_turbine.set3DAttributes(position);
			_turbine.setParametersByIDs(TurbineParameters, _values, _values.Length);
		}

		void AttachHighPass()
		{
			if (_turbine.getChannelGroup(out var group) != RESULT.OK || !group.hasHandle()) return;
			var core = FMODUnity.RuntimeManager.CoreSystem;
			if (core.createDSPByType(DSP_TYPE.MULTIBAND_EQ, out var eq) != RESULT.OK) return;
			eq.setParameterInt((int)DSP_MULTIBAND_EQ.A_FILTER, (int)DSP_MULTIBAND_EQ_FILTER_TYPE.HIGHPASS_24DB);
			eq.setParameterFloat((int)DSP_MULTIBAND_EQ.A_FREQUENCY, WhineHighPassHz);
			if (group.addDSP(0, eq) != RESULT.OK)
			{
				eq.release();
				return;
			}
			_highPass = eq;
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
