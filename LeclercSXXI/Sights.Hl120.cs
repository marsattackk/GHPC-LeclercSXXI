using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;
using MelonLoader;
using Reticle;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace LeclercSXXI
{
	internal static partial class Sights
	{
		static void FitCommander(
			Vehicle vehicle,
			Transform turret,
			Fitted fitted,
			UsableOptic gunnerDay,
			UsableOptic gunnerNight,
			Transform gunnerLaser)
		{
			// The HL 120 head is a static prop. No views, no fire-control link, no head drive. The mesh stays in the bundle; with
			// no CommanderCupola nothing turns it. The free-look commander view sat too low; raised so the eyes clear the roof.
			var lookSlot = vehicle.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(slot => slot.name == "commander head");
			if (lookSlot != null)
			{
				// The eyes belong over the commander's hatch ring (CommanderHead), not the donor spot.
				var eye = vehicle.transform.TransformPoint(CommanderHead + Vector3.up * CommanderViewRaise);
				// The eyes go above the HL 120 head (the commander's camera), not level with it.
				var headTop = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == HeadGroup)?
					.GetComponentsInChildren<Renderer>(true)
						.Select(renderer => renderer.bounds.max.y)
						.DefaultIfEmpty(float.NaN)
						.Max() ?? float.NaN;
				if (float.IsNaN(headTop)) headTop = vehicle.transform.TransformPoint(new Vector3(0f, HeadTopFallback, 0f)).y;
				eye.y = Mathf.Max(eye.y, headTop + CommanderViewClear);
				lookSlot.transform.position = eye;
				if (lookSlot.LinkedNightSight != null) lookSlot.LinkedNightSight.transform.position = eye;
			}
			return;
#pragma warning disable CS0162
			var root = vehicle.transform;
			var mount = LeclercSXXIMod.Rig?.Mounts.FirstOrDefault(entry => entry.Group == HeadGroup);
			var head = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == HeadGroup);
			if (mount == null || head == null)
			{
				MelonLogger.Warning($"{Tag}: no HL 120 head in the bundle; commander's sights left as they were.");
				return;
			}

			var slots = vehicle.GetComponentsInChildren<CameraSlot>(true);
			var free = slots.FirstOrDefault(slot => slot.name == "commander head");
			var nods = free?.LinkedNightSight;
			if (free != null) free.transform.position = root.TransformPoint(CommanderHead);
			if (nods != null) nods.transform.position = root.TransformPoint(CommanderHead);
			var eyes = new GameObject("HL 120 optic frame").transform;
			eyes.SetParent(turret, false);
			eyes.SetPositionAndRotation(root.TransformPoint(CommanderEye), root.rotation);
			var window = new GameObject("HL 120 laser window").transform;
			window.SetParent(head, false);
			window.position = root.TransformPoint(CommanderEye);
			fitted.Head = head;

			// The commander's fire-control views share the main gun's computer. The free-look commander view retains
			// independent observation and Aim Override designates its line to the gun.
			var weapons = vehicle.GetComponent<WeaponsManager>().Weapons.Where(info => info.Role == WeaponSystemRole.MainGun
				|| info.Role == WeaponSystemRole.Coaxial)
				.Select(info => info.Weapon).Where(weapon => weapon != null).ToArray();
			var holder = new GameObject("HL 120 setup");
			holder.SetActive(false);
			UsableOptic Copy(UsableOptic source, string name)
			{
				var copy = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
				copy.SetActive(false);
				copy.name = name;
				DonorParts.Scrub(copy, root);
				foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
				var optic = copy.GetComponent<UsableOptic>();
				optic.FCS = fitted.Fcs;
				optic.RegisterAtStart = false;
				optic.Alignment = OpticAlignment.BoresightStabilized;
				optic.RotateElevation = optic.RotateAzimuth = true;
				optic.CantCorrect = true;
				optic.ForceHorizontalReticleAlign = true;
				optic.LocalElevationLimits = CommanderPitch;
				optic.LocalAzimuthLimits = Vector2.zero;
				optic.OverrideRangeLimits = Vector2.zero;
				optic.OverrideRangeStep = 0;
				optic.Mounts = Array.Empty<AimablePlatform>();
				optic.postAdditional = optic.postAdditional?.Where(volume => volume != null).ToArray() ?? Array.Empty<PostProcessVolume>();
				optic.transform.SetParent(eyes, false);
				optic.transform.localPosition = Vector3.zero;
				optic.transform.localRotation = Quaternion.identity;
				var cameraSlot = optic.slot;
				cameraSlot.transform.SetPositionAndRotation(eyes.position, eyes.rotation);
				Reparent?.SetValue(cameraSlot, cameraSlot.transform == optic.transform ? eyes : cameraSlot.transform.parent);
				cameraSlot.ExclusiveWeapons = weapons;
				cameraSlot.ExclusiveItems = Array.Empty<GameObject>();
				cameraSlot.ZeroScaleItems = Array.Empty<Transform>();
				cameraSlot.SlotToUseOnDestroy = gunnerDay.slot;
				cameraSlot.AllowFreeLook = false;
				cameraSlot.IsExterior = false;
				cameraSlot.DirectAttachOnly = false;
				cameraSlot.LinkedNightSight = null;
				return optic;
			}
			// The T-80B's day camera lives on the FCS, outside its GPS UsableOptic. The night optic contains
			// its camera, so both copies use that as their host. Give the day host the day reticle renderer and post.
			var day = Copy(gunnerNight, "HL 120 day");
			var night = Copy(gunnerNight, "HL 120 Iris thermal");
			var oldMarks = day.reticleMesh.gameObject;
			day.reticleMesh = UnityEngine.Object
				.Instantiate(gunnerDay.reticleMesh.gameObject, day.transform, false)
				.GetComponent<ReticleMesh>();
			day.reticleMesh.transform.localPosition = Vector3.zero;
			UnityEngine.Object.DestroyImmediate(oldMarks);
			day.AltNightPost = null;
			day.slot.VisionType = NightVisionType.None;
			day.slot.BaseBlur = gunnerDay.slot.BaseBlur;
			day.slot.VibrationPreBlur = gunnerDay.slot.VibrationPreBlur;
			ConfigureDay(day, CommanderWide, CommanderNarrow, _dayMarks, copy: true);
			GunnerFcs.AimClamp.Register(fitted.Fcs, day);
			fitted.CommanderDay = day;
			if (ConfigureThermal(day, night, "HL 120", CommanderPitch, turret))
			{
				day.slot.LinkedNightSight = night.slot;
				fitted.CommanderThermal = night;
				night.gameObject.SetActive(true);   // a day slot sets its linked-night flags in Awake
			}
			else UnityEngine.Object.Destroy(night.gameObject);
			day.gameObject.SetActive(true);
			UnityEngine.Object.Destroy(holder);
			GunnerFcs.HeadView(Tag, window, day.slot.transform, fitted.CommanderThermal?.slot.transform);
			GunnerFcs.LaserAim.FromWhile(fitted.Fcs, day.slot, window, Settings.LaserMax);
			if (fitted.CommanderThermal != null) GunnerFcs.LaserAim.FromWhile(fitted.Fcs, night.slot, window, Settings.LaserMax);
			// Main gun convergence needs the commander's window while either channel is in use.
			var reference = fitted.Fcs.ReferenceTransform?.GetComponent<GunnerFcs.HeadPoint>();
			void SwitchReference(bool active)
			{
				if (reference == null) return;
				var point = active ? window : gunnerLaser;
				reference.Turret = point.parent;
				reference.Local = point.localPosition;
				reference.Put();
			}
			day.slot.ActiveChanged += SwitchReference;
			if (fitted.CommanderThermal != null) night.slot.ActiveChanged += SwitchReference;
			try { FitObserve(fitted, free, window, day, fitted.CommanderThermal); }
			catch (Exception exception) { MelonLogger.Error($"{Tag}: HL 120 observation views failed: {exception}"); }
			var link = TargetDesignation.Register(vehicle, fitted.Fcs, new[] { free, nods, fitted.ObserveDay, fitted.ObserveThermal }, Tag);
			fitted.HeadDrive = CommanderCupola.Apply(vehicle, new CommanderCupola.Settings
			{
				Tag = Tag,
				Bone = head,
				Pivot = mount.Pivot,
				Axis = mount.Axis,
				Speed = 90f,   // inferred; only the head, no hatch or collar
				Look = new[] { free, nods, day.slot, fitted.CommanderThermal?.slot, fitted.ObserveDay, fitted.ObserveThermal }
					.Where(slot => slot != null)
					.ToArray(),
				Cu = link,
			});
			RemoteMg.LateSetup(vehicle);
		}

		// Not in play: FitCommander returns before it, so the HL 120 has no views.
		// The HL 120's own observation (two extra views, the gun-laying HL 120 views kept): the commander
		// scans through the head's day and Iris channels while the gun stays where the gunner has it; Aim Override hands the
		// line to the gun (hunter-killer, «le viseur du tireur s'aligne sur l'objectif pendant que le chef de char continue son
		// observation» (French Wikipedia, Leclerc)). GHPC lets the player look around only in a free-look view, so these
		// are free-look views (the commander's own view cycle, beside "commander head") on the head's window: the head follows
		// the look (CommanderCupola), the zoom steps are the HL 120's fields, the picture its day / thermal look. No reticle
		// (a free-look view has no optic to draw one): inferred limit.
		static void FitObserve(Fitted fitted, CameraSlot free, Transform window, UsableOptic day, UsableOptic night)
		{
			if (free == null)
			{
				MelonLogger.Warning($"{Tag}: no free-look commander view; no HL 120 observation views.");
				return;
			}
			CameraSlot Make(string name, CameraSlot like, float fov, float[] others)
			{
				var viewObject = new GameObject(name) { layer = free.gameObject.layer };
				viewObject.SetActive(false);   // fields first: CameraSlot.Awake reads them
				viewObject.transform.SetParent(window, false);
				var view = viewObject.AddComponent<CameraSlot>();
				view.AllowFreeLook = true;
				view.AllowFreeZoom = false;   // the field steps; a free zoom brings GHPC's binoculars
				view.DefaultFov = fov;
				view.OtherFovs = others ?? Array.Empty<float>();
				view.fovAspect = like.fovAspect;
				view.WrapFovLevels = false;
				view.BaseBlur = like.BaseBlur;
				view.VibrationBlurScale = like.VibrationBlurScale;
				view.VibrationPreBlur = like.VibrationPreBlur;
				view.VibrationShakeMultiplier = like.VibrationShakeMultiplier;
				view.SpriteType = like.SpriteType;
				view.ZeroScaleItems = Array.Empty<Transform>();
				view.ExclusiveItems = Array.Empty<GameObject>();
				view.ExclusiveWeapons = Array.Empty<WeaponSystem>();
				view.ZoomInAudioEvent = like.ZoomInAudioEvent;
				view.ZoomOutAudioEvent = like.ZoomOutAudioEvent;
				view.SlotToUseOnDestroy = free;
				view.IsExterior = false;
				return view;
			}
			var dayView = Make("HL 120 observe day", day.slot, CommanderWide, new[] { CommanderNarrow });
			Posts(dayView, day);
			if (night != null)
			{
				var nightSlot = night.slot;
				var thermalView = Make("HL 120 observe Iris thermal", nightSlot, nightSlot.DefaultFov, nightSlot.OtherFovs);
				thermalView.VisionType = NightVisionType.Thermal;
				thermalView.OverrideFLIRResolution = true;
				thermalView.FLIRWidth = nightSlot.FLIRWidth;
				thermalView.FLIRHeight = nightSlot.FLIRHeight;
				thermalView.FLIRFilterMode = nightSlot.FLIRFilterMode;
				thermalView.FLIRBlitMaterialOverride = nightSlot.FLIRBlitMaterialOverride;
				thermalView.CanToggleFlirPolarity = nightSlot.CanToggleFlirPolarity;
				thermalView.FLIRPolarityOnAudioEvent = nightSlot.FLIRPolarityOnAudioEvent;
				thermalView.FLIRPolarityOffAudioEvent = nightSlot.FLIRPolarityOffAudioEvent;
				thermalView.SlotToUseOnDestroy = free;
				Posts(thermalView, night);
				thermalView.gameObject.AddComponent<ThermalSight.Resolution>()
					.Init(thermalView, ThermalZoom, ThermalWidth, ThermalHeight);
				dayView.LinkedNightSight = thermalView;
				dayView.NightSightAtNightOnly = false;
				thermalView.gameObject.SetActive(true);   // before the day view: its Awake marks the linked night view
				fitted.ObserveThermal = thermalView;
			}
			dayView.gameObject.SetActive(true);
			fitted.ObserveDay = dayView;
		}

		// The optic's picture (its post volumes) on a view that has no optic: copies, switched with the view.
		static void Posts(CameraSlot view, UsableOptic from)
		{
			var volumes = new List<PostProcessVolume>();
			foreach (var source in new[] { from.post }.Concat(from.postAdditional ?? Array.Empty<PostProcessVolume>()))
			{
				if (source == null) continue;
				var volumeObject = new GameObject(source.name) { layer = source.gameObject.layer };
				volumeObject.transform.SetParent(view.transform, false);
				var copy = volumeObject.AddComponent<PostProcessVolume>();
				copy.isGlobal = true;
				copy.weight = source.weight;
				copy.priority = source.priority;
				copy.sharedProfile = source.sharedProfile;
				copy.enabled = false;
				volumes.Add(copy);
			}
			view.ActiveChanged += on =>
			{
				foreach (var copy in volumes)
				{
					if (copy != null) copy.enabled = on;
				}
			};
		}
	}
}
