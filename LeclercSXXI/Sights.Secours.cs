using System;
using System.Linq;
using GHPC;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace LeclercSXXI
{
	internal static partial class Sights
	{
		// The HL 120 emergency gun-laying view: a backup sight working like the Abrams' one. The real Leclerc has none for the
		// gunner: the commander takes over through his sight head (armedconflicts.com, topic 11932). The head stays a static
		// prop facing forward (the turret carries the gun's azimuth), but its mirror follows the gun (Boresight on the fire
		// control's aim, no range angle or lead while in use: EmergencySight), so the reticle's hand scales give the lay. Day
		// channel only, the HL 120 narrow field as the emergency one (x10 borrowed), the wide one kept. Always in the gunner's
		// view cycle. Inferred: no convergence (the window is ~0.5 m left of and ~0.6 m above the bore, like the Abrams' 0.28 m).
		static void FitSecours(Vehicle vehicle, Transform turret, Fitted fitted, UsableOptic gunnerDay, UsableOptic gunnerNight)
		{
			var root = vehicle.transform;
			var window = new GameObject("HL 120 secours window").transform;
			window.SetParent(turret, false);
			window.SetPositionAndRotation(root.TransformPoint(CommanderEye), root.rotation);
			var weapons = vehicle.GetComponent<WeaponsManager>().Weapons.Where(info => info.Role == WeaponSystemRole.MainGun
				|| info.Role == WeaponSystemRole.Coaxial)
				.Select(info => info.Weapon).Where(weapon => weapon != null).ToArray();
			var holder = new GameObject("HL 120 secours setup");
			holder.SetActive(false);
			// The T-80B's day camera lives on the FCS, outside its GPS optic. The night optic carries its own camera, so it is the host.
			var copy = UnityEngine.Object.Instantiate(gunnerNight.gameObject, holder.transform, false);
			copy.name = "HL 120 secours";
			DonorParts.Scrub(copy, root);
			foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
			var optic = copy.GetComponent<UsableOptic>();
			if (optic?.slot == null || !optic.slot.transform.IsChildOf(copy.transform))
			{
				DonorParts.WarnOnce(Tag, $"{Tag}: the base night optic's view is not inside it; no HL 120 emergency view.");
				UnityEngine.Object.Destroy(holder);
				return;
			}
			optic.FCS = fitted.Fcs;
			optic.RegisterAtStart = false;
			optic.Alignment = OpticAlignment.BoresightStabilized;
			optic.RotateElevation = optic.RotateAzimuth = true;
			optic.CantCorrect = false;
			optic.ForceHorizontalReticleAlign = true;
			optic.LocalElevationLimits = CommanderPitch;
			optic.LocalAzimuthLimits = Vector2.zero;
			optic.OverrideRangeLimits = Vector2.zero;
			optic.OverrideRangeStep = 0;
			optic.Mounts = Array.Empty<AimablePlatform>();
			optic.postAdditional = Array.Empty<PostProcessVolume>();
			optic.AltNightPost = null;
			optic.transform.SetParent(window, false);
			optic.transform.localPosition = Vector3.zero;
			optic.transform.localRotation = Quaternion.identity;
			var cameraSlot = optic.slot;
			cameraSlot.transform.SetPositionAndRotation(window.position, window.rotation);
			Reparent?.SetValue(cameraSlot, cameraSlot.transform == optic.transform ? window : cameraSlot.transform.parent);
			cameraSlot.ExclusiveWeapons = weapons;
			cameraSlot.ExclusiveItems = Array.Empty<GameObject>();
			cameraSlot.ZeroScaleItems = Array.Empty<Transform>();
			cameraSlot.SlotToUseOnDestroy = null;
			cameraSlot.HitZone = null;                  // not the base night sight's box: the HL 120's head ends it
			cameraSlot.AllowFreeLook = false;
			cameraSlot.IsExterior = false;
			cameraSlot.DirectAttachOnly = false;
			cameraSlot.LinkedNightSight = null;
			cameraSlot.VisionType = NightVisionType.None;
			cameraSlot.BaseBlur = gunnerDay.slot.BaseBlur;
			cameraSlot.VibrationPreBlur = gunnerDay.slot.VibrationPreBlur;
			// The day reticle renderer in place of the night sight's.
			var oldMarks = optic.reticleMesh != null ? optic.reticleMesh.gameObject : null;
			optic.reticleMesh = UnityEngine.Object
				.Instantiate(gunnerDay.reticleMesh.gameObject, optic.transform, false)
				.GetComponent<ReticleMesh>();
			optic.reticleMesh.transform.localPosition = Vector3.zero;
			if (oldMarks != null) UnityEngine.Object.DestroyImmediate(oldMarks);
			ConfigureDay(optic, CommanderWide, CommanderNarrow, _dayMarks, copy: true);
			GunnerFcs.AimClamp.Register(fitted.Fcs, optic);
			copy.SetActive(true);
			copy.transform.SetParent(window, false);
			UnityEngine.Object.Destroy(holder);
			fitted.Secours = optic;
			EmergencySight.Attach(vehicle, optic, fitted.Fcs);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevFitted(Vehicle vehicle, Fitted fitted);
	}
}
