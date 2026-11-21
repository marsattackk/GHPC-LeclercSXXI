using System;
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
		const string Tag = LeclercSXXIMod.FriendlyName;
		const string HeadGroup = "HL120_HEAD";
		// On the roof window. The outlet is inferred from the centre of the two apertures in the visual model, 23 mm ahead of
		// the shield. The two channels and laser share the same point in GHPC.
		internal static readonly Vector3 GunnerEye = new Vector3(0.563f, 2.203f, 0.804f);
		// The window in the rotating head, a little ahead of its front face.
		internal static readonly Vector3 CommanderEye = new Vector3(-0.478f, 2.715f, 0.056f);
		const float CommanderViewRaise = 0.15f;   // metres above CommanderHead: eye height over the hatch ring
		// The commander's hatch ring, my estimate from the model's commander hatch (War Thunder's hatch_01, turret left).
		static readonly Vector3 CommanderHead = new Vector3(-0.735f, 2.565f, -0.895f);
		const float CommanderViewClear = 0.20f;   // metres the free-look eye sits above the HL 120 head's top
		const float HeadTopFallback = 2.902f;     // HL 120 top from its damage box (modules_built.json) when no mesh is found
		const float DayWide = 17.5f, DayNarrow = 5.5f;   // French Wikipedia, Leclerc
		const float CommanderWide = 20f, CommanderNarrow = 5f;   // the HL 70 day fields borrowed for the HL 120
		const float ThermalWide = 6f, ThermalNarrow = 2f, ThermalZoom = 1f;   // vertical angles, and x20 is inferred
		// Iris: 576 lines. Render at 16:9 with the M1IP's scope and the shared clean thermal image.
		const int ThermalWidth = 1024, ThermalHeight = 576;
		// The HL 130's displays (from pictures of the real sight, HlDisplay): the day box's green seven segments, the thermal row.
		const string DayDisplay = "hl130_day.jsonc", ThermalDisplay = "hl130_thermal.jsonc";
		// The green of the pictures' seven segments, the exact shade my choice.
		static readonly Color DayLit = new Color(0.42f, 1f, 0.38f);
		static readonly Vector2 CommanderPitch = new Vector2(-10f, 25f);   // inferred: no source for the HL 120 mirror travel
		static readonly GunnerFcs.Settings Settings = new GunnerFcs.Settings
		{
			Tag = Tag,
			LaserMax = 6000f,                  // my choice: no source gives the HL 130's laser range
			// Relative to the turret (MountOnTurret), inferred: the gun's -8 / +20 with room below.
			LineOfSightLimits = new Vector2(-10f, 20f),
			LineOfSightAzimuth = 5f,           // inferred, to keep the lead and laser within the forward window
			ViewStopsAtAzimuth = true,
			LeadLimit = 5f,
			LeadLikeM1 = true,                 // the vanilla M1's lead: Smooth Aim held at the lase, dropped when let go
			ManualMode = false,                // the M1IP's cross stays on the line of sight at a hand-set range
		};

		static readonly System.Reflection.FieldInfo Reparent = Reflect.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly System.Reflection.FieldInfo FovItems = Reflect.Field(typeof(UsableOptic), "FovLimitedItems");
		static readonly System.Reflection.FieldInfo ExtraReticles = Reflect.Field(typeof(UsableOptic), "AdditionalReticleMeshes");

		internal sealed class Fitted : RemoteMg.Station
		{
			internal FireControlSystem Fcs;
			internal Transform Turret, Laser;   // set at spawn (Apply), read when the views are fitted (Fit)
			internal UsableOptic GunnerDay, GunnerThermal, CommanderDay, CommanderThermal;
			internal CameraSlot ObserveDay, ObserveThermal;   // the HL 120's own observation, the gun left where it is
			internal UsableOptic Secours;   // the HL 120 slaved to the gun, emergency reticle (FitSecours)
			internal Transform Head;
			internal CommanderCupola HeadDrive;
			// The views made after Unit.Awake listed the unit's camera slots: without this list (RemoteMg.LateSetup and the
			// camera manager patch) Cycle View never reaches them (the HL 120 secours). A view added after spawn is in Cycle
			// View only if it is listed here.
			internal override IEnumerable<CameraSlot> Sights => new[] { CommanderDay?.slot, Secours?.slot }.Where(slot => slot != null);
		}

		static ReticleSO _dayMarks;   // the M1IP's: the HL 120 secours view's, and the HL 130's if its own can't be built
		static CameraSpriteManager.SpriteType _dayMask, _thermalMask;
		static PostProcessProfile _dayPicture;
		static float _dayPicturePriority;

		internal static void WarmDonors()
			=> DonorParts.WarmAtMenu(Tag, () => LoadMarks(), () => ThermalSight.WarmDonor(Tag), ConversionVoices.WarmDonor, Coax.WarmDonor,
				() => SmokeGrenadeDonor.Warm(Tag, Countermeasures.SmokeFrom));

		static bool LoadMarks()
		{
			if (_dayMarks != null) return true;
			var donor = DonorParts.Prefab("M1IP");
			var main = donor?.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(info => info?.Role == WeaponSystemRole.MainGun)?.FCS;
			var optics = donor?.GetComponentsInChildren<UsableOptic>(true);
			var day = optics?.FirstOrDefault(optic => optic.FCS == main && optic.slot != null && optic.slot.LinkedNightSight != null);
			var night = optics?.FirstOrDefault(optic => day != null && optic.slot == day.slot.LinkedNightSight);
			var daySource = day?.reticleMesh?.reticleSO;
			if (daySource == null || night?.slot == null) return false;
			_dayMarks = OwnMarks(daySource);
			_dayMask = day.slot.SpriteType;
			_thermalMask = night.slot.SpriteType;
			_dayPicture = day.post?.sharedProfile;
			_dayPicturePriority = day.post != null ? day.post.priority : 0f;
			return true;
		}

		static ReticleSO OwnMarks(ReticleSO source)
		{
			// GHPC caches generated reticle meshes by name.
			var copy = UnityEngine.Object.Instantiate(source);
			copy.name = "Leclerc SXXI (" + source.name + ")";
			return copy;
		}

		// At spawn, after moving the rig's axes, with the turret at rest: the HL 130's laser window. The views are fitted two
		// frames later (Fit, a late step), once the base optics have started.
		internal static void Apply(Vehicle vehicle)
		{
			var turret = vehicle.transform.Find("T80B_rig/HULL/TURRET");
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(info => info?.Role == WeaponSystemRole.MainGun)?.FCS;
			if (turret == null || main == null)
			{
				DonorParts.WarnOnce(Tag, $"{Tag}: no turret or main gun fire control; sights left as they were.");
				return;
			}
			if (vehicle.GetComponentInChildren<Fitted>(true) != null) return;
			var fitted = vehicle.gameObject.AddComponent<Fitted>();
			fitted.Fcs = main;
			fitted.Turret = turret;
			fitted.Laser = GunnerFcs.LaserWindow(vehicle.transform, main, turret, GunnerEye, "HL 130 laser window");
		}

		// Two frames after the spawn (Conversion.LateSteps): the HL 130's and HL 120's views on the base optics.
		internal static void Fit(Vehicle vehicle)
		{
			var fitted = vehicle.GetComponent<Fitted>();
			if (fitted != null) Fit(vehicle, fitted.Turret, fitted.Laser, fitted);   // none: Apply said why
		}

		// After the module wiring (Conversion.LateSteps): the HL 120 head lost (the hl120 module) takes its emergency view with it.
		internal static void EndSecoursWithHead(Vehicle vehicle)
		{
			var fitted = vehicle.GetComponent<Fitted>();
			var head = vehicle.GetComponentsInChildren<ModuleBuilder.ModuleSet>(true).SelectMany(set => set.Built)
				.FirstOrDefault(built => built.m.Id == "hl120").dc;
			var view = fitted?.Secours?.slot;
			if (head != null && view != null)
			{
				head.Destroyed += _ =>
				{
					if (view != null) view.ForceDamage();
				};
			}
			DevFitted(vehicle, fitted);
		}

		static void Fit(Vehicle vehicle, Transform turret, Transform laser, Fitted fitted)
		{
			var optics = vehicle.GetComponentsInChildren<UsableOptic>(true);
			var fcs = fitted.Fcs;
			var day = optics.FirstOrDefault(optic => optic.FCS == fcs
				&& optic.RegisterAtStart
				&& optic.slot != null
				&& optic.slot.LinkedNightSight != null);
			var night = optics.FirstOrDefault(optic => day != null && optic.slot == day.slot.LinkedNightSight);
			if (day == null || night == null || !LoadMarks())
			{
				DonorParts.WarnOnce(Tag, $"{Tag}: no gunner optic pair or M1IP reticles; sight conversion skipped.");
				return;
			}

			// First, from the base optics as they came (the HL 130's own setup below moves its slots onto the turret).
			try { FitSecours(vehicle, turret, fitted, day, night); }
			catch (Exception exception) { MelonLogger.Error($"{Tag}: HL 120 emergency view failed: {exception}"); }
			// Into the unit's camera slots, so Cycle View reaches it (FitCommander's own call is skipped: static head).
			try { RemoteMg.LateSetup(vehicle); }
			catch (Exception exception) { MelonLogger.Error($"{Tag}: HL 120 emergency view not added to Cycle View: {exception}"); }

			fitted.GunnerDay = day;
			day.name = "HL 130 day";
			var dayMarks = GunnerDayReticle.Fit(vehicle, day, _dayMarks);
			ConfigureDay(day, DayWide, DayNarrow, dayMarks);
			// The reticle is drawn at its angular size, so the wide field shrinks it 3x: its lines keep the narrow field's
			// width on the screen there (the marks keep their place and size).
			ReticleWidth.Attach(day, 1f);
			// The display box under the cross, with the HL 130's own reticle only (after ConfigureDay: it clears the display).
			if (dayMarks != _dayMarks) HlDisplay.Attach(day, DayDisplay, turret, DayLit);
			GunnerFcs.Apply(day, Settings);
			GunnerFcs.HeadView(Tag, laser, day.slot.transform, night.slot.transform, fcs.ReferenceTransform);
			// The HL 130 head is on the turret roof: its views don't ride the gun (the reload dip dragged them).
			GunnerFcs.MountOnTurret(Tag, vehicle.transform, turret, day.slot.transform, night.slot.transform);
			GunnerFcs.LaserAim.Register(fcs, day, Settings);
			GunnerFcs.Parallax.Register(fcs);
			// All that's left of the commander's sight: the free-look eye raised over the HL 120 head.
			try { FitCommander(vehicle, turret, fitted, day, night, laser); }
			catch (Exception exception) { MelonLogger.Error($"{Tag}: commander's free-look eye not raised: {exception}"); }
			if (ConfigureThermal(day, night, "HL 130", day.LocalElevationLimits, turret))
			{
				fitted.GunnerThermal = night;
				ThermalSight.SetUnitNightVision(vehicle, night.slot);
			}
			// The HL 130 head lost (the hl130 module): the gunner goes to the commander's HL 120, which lays the same gun. With
			// the HL 120's gun-laying views off (static head), its emergency view.
			var fallback = fitted.CommanderDay?.slot ?? fitted.Secours?.slot;
			if (fallback != null)
			{
				day.slot.SlotToUseOnDestroy = fallback;
				night.slot.SlotToUseOnDestroy = fallback;
			}
		}

		static void ConfigureDay(UsableOptic day, float wide, float narrow, ReticleSO marks, bool copy = false)
		{
			var slot = day.slot;
			slot.DefaultFov = wide;
			slot.OtherFovs = new[] { narrow };
			slot.fovAspect = true;                // day fields across the view
			slot.AllowFreeZoom = false;
			slot.WrapFovLevels = false;
			slot.SpriteType = _dayMask;
			if (day.post != null && _dayPicture != null)
			{
				day.post.sharedProfile = _dayPicture;
				day.post.priority = _dayPicturePriority;
			}
			ReplaceReticle(day, marks, copy);
			SightMotion.Stabilised(slot);
		}

		static bool ConfigureThermal(UsableOptic day, UsableOptic night, string name, Vector2 pitch, Transform turret)
		{
			if (!ThermalSight.Apply(day, night, new ThermalSight.Settings
			{
				Tag = Tag,
				Name = name + " Iris",
				WideFov = ThermalWide,
				OtherFovs = new[] { ThermalNarrow, ThermalZoom },
				ElectronicFov = ThermalZoom,
				Width = ThermalWidth,
				Height = ThermalHeight,
				ElevationLimits = pitch,
				Crt = false,
				// The Iris's marks are electronic, the same size in every field: screen items only (hl130_thermal), no reticle.
				Reticle = optic =>
				{
					ClearDisplay(optic);
					if (optic.reticleMesh != null) optic.reticleMesh.gameObject.SetActive(false);
					HlDisplay.Attach(optic, ThermalDisplay, turret, Color.white);
				},
			})) return false;
			night.name = name + " Iris thermal";
			night.slot.SpriteType = _thermalMask;
			day.slot.NightSightAtNightOnly = false;
			SightMotion.Stabilised(night.slot, keepBlur: true);
			return true;
		}

		static void ReplaceReticle(UsableOptic optic, ReticleSO marks, bool copy)
		{
			var mesh = optic.reticleMesh;
			if (mesh == null) return;
			ClearDisplay(optic);
			mesh.reticleSO = marks;
			ReticleBuild.PrepareCopy(mesh);
			mesh.gameObject.SetActive(true);
			// The existing optic has already run Start even when out of view, so rebuild its mesh now.
			// The commander copies load on their first activation.
			if (!copy) mesh.Load();
		}

		// The base optic's own marks and indicators off: every reticle renderer but the primary one, its in-scope display.
		static void ClearDisplay(UsableOptic optic)
		{
			var mesh = optic.reticleMesh;
			// The 1G42's other reticle objects belong to the Soviet optic. Keep only the primary renderer.
			foreach (var other in optic.GetComponentsInChildren<ReticleMesh>(true))
			{
				if (other == mesh || other.GetComponentInParent<UsableOptic>(true) != optic) continue;
				other.gameObject.SetActive(false);
				other.enabled = false;
			}
			ExtraReticles?.SetValue(optic, Array.Empty<ReticleMesh>());
			FovItems?.SetValue(optic, Array.CreateInstance(FovItems.FieldType.GetElementType(), 0));
			// The T-80B's in-scope indicators are part of its own display. Use GHPC's range HUD with the borrowed marks.
			// The weapon's ammo-type exclusives can switch the Cyrillic text on again, so turn off its parent canvas.
			var oldDisplay = optic.transform.Find("1G42 Canvas");
			if (oldDisplay != null) oldDisplay.gameObject.SetActive(false);
			if (optic.RangeText != null) optic.RangeText.gameObject.SetActive(false);
			if (optic.ReadyToFireObject != null) optic.ReadyToFireObject.SetActive(false);
			if (optic.OverridingObject != null) optic.OverridingObject.SetActive(false);
			optic.RangeText = null;
			optic.ReadyToFireObject = null;
			optic.OverridingObject = null;
		}
	}
}
