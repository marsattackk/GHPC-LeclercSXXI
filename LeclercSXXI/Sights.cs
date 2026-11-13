using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	internal static class Sights
	{
		const string Tag = LeclercSXXIMod.FriendlyName;
		const string HeadGroup = "HL120_HEAD";
		internal static readonly Vector3 GunnerEye = new Vector3(0.563f, 2.203f, 0.804f);
		internal static readonly Vector3 CommanderEye = new Vector3(-0.478f, 2.715f, 0.056f);
		const float CommanderViewRaise = 0.15f;
		static readonly Vector3 CommanderHead = new Vector3(-0.735f, 2.565f, -0.895f);
		const float CommanderViewClear = 0.20f;
		const float HeadTopFallback = 2.902f;
		const float DayWide = 17.5f, DayNarrow = 5.5f;
		const float CommanderWide = 20f, CommanderNarrow = 5f;
		const float ThermalWide = 6f, ThermalNarrow = 2f, ThermalZoom = 1f;
		const int ThermalWidth = 1024, ThermalHeight = 576;
		static readonly Vector2 CommanderPitch = new Vector2(-10f, 25f);
		static readonly GunnerFcs.Settings FcsSettings = new GunnerFcs.Settings
		{
			LaserMax = 6000f,
			LineOfSightLimits = new Vector2(-10f, 20f),
			LineOfSightAzimuth = 5f,
			ViewStopsAtAzimuth = true,
			LeadLimit = 5f,
			ManualMode = false,
		};

		static readonly FieldInfo Reparent = Reflect.Field(typeof(CameraSlot), "_reparentTarget");
		static readonly FieldInfo FovItems = Reflect.Field(typeof(UsableOptic), "FovLimitedItems");
		static readonly FieldInfo ExtraReticles = Reflect.Field(typeof(UsableOptic), "AdditionalReticleMeshes");

		internal sealed class Fitted : RemoteMg.Station
		{
			internal FireControlSystem Fcs;
			internal UsableOptic CommanderDay, CommanderThermal;
			internal CameraSlot ObserveDay, ObserveThermal;
			internal UsableOptic Secours;
			internal override IEnumerable<CameraSlot> Sights => CommanderDay != null ? new[] { CommanderDay.slot } : Array.Empty<CameraSlot>();
		}

		static ReticleSO _dayMarks, _thermalMarks, _thermalWideMarks;
		static CameraSpriteManager.SpriteType _dayMask, _thermalMask;
		static PostProcessProfile _dayPicture;
		static float _dayPicturePriority;

		internal static void WarmDonors()
			=> DonorParts.WarmAtMenu(Tag, () => LoadMarks(), () => ThermalSight.WarmDonor(Tag), Voices.WarmDonor, Coax.WarmDonor,
				() => SmokeGrenadeDonor.Warm(Tag, LeclercSXXIMod.SmokeFrom));

		static bool LoadMarks()
		{
			if (_dayMarks != null && _thermalMarks != null) return true;
			var donor = DonorParts.Prefab("M1IP");
			var main = donor?.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(w => w?.Role == WeaponSystemRole.MainGun)?.FCS;
			var optics = donor?.GetComponentsInChildren<UsableOptic>(true);
			var day = optics?.FirstOrDefault(o => o.FCS == main && o.slot != null && o.slot.LinkedNightSight != null);
			var night = optics?.FirstOrDefault(o => day != null && o.slot == day.slot.LinkedNightSight);
			var daySource = day?.reticleMesh?.reticleSO;
			var nightSource = night?.reticleMesh?.reticleSO;
			if (daySource == null || nightSource == null) return false;
			_dayMarks = OwnMarks(daySource);
			_thermalMarks = OwnMarks(nightSource);
			var wide = night.GetComponentsInChildren<ReticleMesh>(true).FirstOrDefault(m => m.reticleSO != null && m.reticleSO.name == "WFOV");
			_thermalWideMarks = wide != null ? OwnMarks(wide.reticleSO) : _thermalMarks;
			_dayMask = day.slot.SpriteType;
			_thermalMask = night.slot.SpriteType;
			_dayPicture = day.post?.sharedProfile;
			_dayPicturePriority = day.post != null ? day.post.priority : 0f;
			return true;
		}

		static ReticleSO OwnMarks(ReticleSO source)
		{
			var copy = UnityEngine.Object.Instantiate(source);
			copy.name = "Leclerc SXXI (" + source.name + ")";
			return copy;
		}

		internal static void Apply(Vehicle vehicle)
		{
			var turret = vehicle.transform.Find("T80B_rig/HULL/TURRET");
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons?.FirstOrDefault(w => w?.Role == WeaponSystemRole.MainGun)?.FCS;
			if (turret == null || main == null)
			{
				MelonLogger.Warning($"{Tag}: no turret or main gun fire control; sights left as they were.");
				try { Interior.Modules.Wire(vehicle); }
				catch (Exception e) { MelonLogger.Error($"{Tag}: modules failed: {e}"); }
				return;
			}
			if (vehicle.GetComponentInChildren<Fitted>(true) != null) return;
			var fitted = vehicle.gameObject.AddComponent<Fitted>();
			fitted.Fcs = main;
			var laser = GunnerFcs.LaserWindow(vehicle.transform, main, turret, GunnerEye, "HL 130 laser window");
			MelonCoroutines.Start(FitWhenReady(vehicle, turret, laser, fitted));
		}

		static IEnumerator FitWhenReady(Vehicle vehicle, Transform turret, Transform laser, Fitted st)
		{
			yield return null;
			yield return null;
			if (vehicle == null) yield break;
			try { Fit(vehicle, turret, laser, st); }
			catch (Exception e) { MelonLogger.Error($"{Tag}: sight setup failed: {e}"); }
			try { Interior.Modules.Wire(vehicle); }
			catch (Exception e) { MelonLogger.Error($"{Tag}: modules failed: {e}"); }
			try
			{
				var head = vehicle.GetComponentsInChildren<ModuleBuilder.ModuleSet>(true).SelectMany(s => s.Built)
					.FirstOrDefault(b => b.m.Id == "hl120").dc;
				var view = st.Secours?.slot;
				if (head != null && view != null) head.Destroyed += _ => { if (view != null) view.ForceDamage(); };
			}
			catch (Exception e) { MelonLogger.Error($"{Tag}: HL 120 secours hit zone failed: {e}"); }
		}

		static void FitSecours(Vehicle v, Transform turret, Fitted st, UsableOptic gunnerDay, UsableOptic gunnerNight)
		{
			var root = v.transform;
			var window = new GameObject("HL 120 secours window").transform;
			window.SetParent(turret, false);
			window.SetPositionAndRotation(root.TransformPoint(CommanderEye), root.rotation);
			var weapons = v.GetComponent<WeaponsManager>().Weapons.Where(w => w.Role == WeaponSystemRole.MainGun || w.Role == WeaponSystemRole.Coaxial)
				.Select(w => w.Weapon).Where(w => w != null).ToArray();
			var holder = new GameObject("HL 120 secours setup");
			holder.SetActive(false);
			var go = UnityEngine.Object.Instantiate(gunnerNight.gameObject, holder.transform, false);
			go.name = "HL 120 secours";
			DonorParts.Scrub(go, root);
			foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
			var o = go.GetComponent<UsableOptic>();
			if (o?.slot == null || !o.slot.transform.IsChildOf(go.transform))
			{
				MelonLogger.Warning($"{Tag}: the base night optic's view is not inside it; no HL 120 emergency view.");
				UnityEngine.Object.Destroy(holder);
				return;
			}
			o.FCS = st.Fcs;
			o.RegisterAtStart = false;
			o.Alignment = OpticAlignment.BoresightStabilized;
			o.RotateElevation = o.RotateAzimuth = true;
			o.CantCorrect = false;
			o.ForceHorizontalReticleAlign = true;
			o.LocalElevationLimits = CommanderPitch;
			o.LocalAzimuthLimits = Vector2.zero;
			o.OverrideRangeLimits = Vector2.zero;
			o.OverrideRangeStep = 0;
			o.Mounts = Array.Empty<AimablePlatform>();
			o.postAdditional = Array.Empty<PostProcessVolume>();
			o.AltNightPost = null;
			o.transform.SetParent(window, false);
			o.transform.localPosition = Vector3.zero;
			o.transform.localRotation = Quaternion.identity;
			var s = o.slot;
			s.transform.SetPositionAndRotation(window.position, window.rotation);
			Reparent?.SetValue(s, s.transform == o.transform ? window : s.transform.parent);
			s.ExclusiveWeapons = weapons;
			s.ExclusiveItems = Array.Empty<GameObject>();
			s.ZeroScaleItems = Array.Empty<Transform>();
			s.SlotToUseOnDestroy = null;
			s.HitZone = null;
			s.AllowFreeLook = false;
			s.IsExterior = false;
			s.DirectAttachOnly = false;
			s.LinkedNightSight = null;
			s.VisionType = NightVisionType.None;
			s.BaseBlur = gunnerDay.slot.BaseBlur;
			s.VibrationPreBlur = gunnerDay.slot.VibrationPreBlur;
			var oldMarks = o.reticleMesh != null ? o.reticleMesh.gameObject : null;
			o.reticleMesh = UnityEngine.Object.Instantiate(gunnerDay.reticleMesh.gameObject, o.transform, false).GetComponent<ReticleMesh>();
			o.reticleMesh.transform.localPosition = Vector3.zero;
			if (oldMarks != null) UnityEngine.Object.DestroyImmediate(oldMarks);
			ConfigureDay(o, CommanderWide, CommanderNarrow, _dayMarks, copy: true);
			GunnerFcs.AimClamp.Register(st.Fcs, o);
			go.SetActive(true);
			go.transform.SetParent(window, false);
			UnityEngine.Object.Destroy(holder);
			st.Secours = o;
			EmergencySight.Attach(v, o, st.Fcs);
		}

		static void Fit(Vehicle v, Transform turret, Transform laser, Fitted st)
		{
			var optics = v.GetComponentsInChildren<UsableOptic>(true);
			var fcs = st.Fcs;
			var day = optics.FirstOrDefault(o => o.FCS == fcs && o.RegisterAtStart && o.slot != null && o.slot.LinkedNightSight != null);
			var night = optics.FirstOrDefault(o => day != null && o.slot == day.slot.LinkedNightSight);
			if (day == null || night == null || !LoadMarks())
			{
				MelonLogger.Warning($"{Tag}: no gunner optic pair or M1IP reticles; sight conversion skipped.");
				return;
			}

			try { FitSecours(v, turret, st, day, night); }
			catch (Exception e) { MelonLogger.Error($"{Tag}: HL 120 emergency view failed: {e}"); }
			day.name = "HL 130 day";
			ConfigureDay(day, DayWide, DayNarrow, GunnerDayReticle.Fit(v, day, _dayMarks));
			GunnerFcs.Apply(day, FcsSettings);
			GunnerFcs.HeadView(Tag, laser, day.slot.transform, night.slot.transform, fcs.ReferenceTransform);
			GunnerFcs.MountOnTurret(Tag, v.transform, turret, day.slot.transform, night.slot.transform);
			GunnerFcs.LaserAim.Register(fcs, day, FcsSettings);
			GunnerFcs.Parallax.Register(fcs);
			try { FitCommander(v, turret, st, day, night, laser); }
			catch (Exception e) { MelonLogger.Error($"{Tag}: commander sight setup failed: {e}"); }
			if (ConfigureThermal(day, night, "HL 130", day.LocalElevationLimits)) ThermalSight.SetUnitNightVision(v, night.slot);
			var fallback = st.CommanderDay?.slot ?? st.Secours?.slot;
			if (fallback != null)
			{
				day.slot.SlotToUseOnDestroy = fallback;
				night.slot.SlotToUseOnDestroy = fallback;
			}
		}

		static void ConfigureDay(UsableOptic day, float wide, float narrow, ReticleSO marks, bool copy = false)
		{
			var s = day.slot;
			s.DefaultFov = wide;
			s.OtherFovs = new[] { narrow };
			s.fovAspect = true;
			s.AllowFreeZoom = false;
			s.WrapFovLevels = false;
			s.SpriteType = _dayMask;
			if (day.post != null && _dayPicture != null)
			{
				day.post.sharedProfile = _dayPicture;
				day.post.priority = _dayPicturePriority;
			}
			ReplaceReticle(day, marks, copy);
			SightMotion.Stabilised(s);
		}

		static bool ConfigureThermal(UsableOptic day, UsableOptic night, string name, Vector2 pitch, bool copy = false)
		{
			if (!ThermalSight.Apply(day, night, new ThermalSight.Settings
			{
				Tag = Tag, Name = name + " Iris",
				WideFov = ThermalWide, OtherFovs = new[] { ThermalNarrow, ThermalZoom }, ElectronicFov = ThermalZoom,
				Width = ThermalWidth, Height = ThermalHeight, ElevationLimits = pitch,
				Crt = false,
				Reticle = o =>
				{
					ReplaceReticle(o, o.slot.FovIndex == 0 ? _thermalWideMarks : _thermalMarks, copy);
					ZoomReticles.Attach(o, new[] { _thermalWideMarks, _thermalMarks, _thermalMarks }, new GameObject[3][]);
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
			foreach (var other in optic.GetComponentsInChildren<ReticleMesh>(true))
			{
				if (other == mesh || other.GetComponentInParent<UsableOptic>(true) != optic) continue;
				other.gameObject.SetActive(false);
				other.enabled = false;
			}
			ExtraReticles?.SetValue(optic, Array.Empty<ReticleMesh>());
			FovItems?.SetValue(optic, Array.CreateInstance(FovItems.FieldType.GetElementType(), 0));
			var oldDisplay = optic.transform.Find("1G42 Canvas");
			if (oldDisplay != null) oldDisplay.gameObject.SetActive(false);
			if (optic.RangeText != null) optic.RangeText.gameObject.SetActive(false);
			if (optic.ReadyToFireObject != null) optic.ReadyToFireObject.SetActive(false);
			if (optic.OverridingObject != null) optic.OverridingObject.SetActive(false);
			optic.RangeText = null;
			optic.ReadyToFireObject = null;
			optic.OverridingObject = null;
			mesh.reticleSO = marks;
			ReticleBuild.PrepareCopy(mesh);
			mesh.gameObject.SetActive(true);
			if (!copy) mesh.Load();
		}

		static void FitCommander(Vehicle v, Transform turret, Fitted st, UsableOptic gunnerDay, UsableOptic gunnerNight, Transform gunnerLaser)
		{
			var lookSlot = v.GetComponentsInChildren<CameraSlot>(true).FirstOrDefault(s => s.name == "commander head");
			if (lookSlot != null)
			{
				var eye = v.transform.TransformPoint(CommanderHead + Vector3.up * CommanderViewRaise);
				var headTop = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HeadGroup)?
					.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds.max.y).DefaultIfEmpty(float.NaN).Max() ?? float.NaN;
				if (float.IsNaN(headTop)) headTop = v.transform.TransformPoint(new Vector3(0f, HeadTopFallback, 0f)).y;
				eye.y = Mathf.Max(eye.y, headTop + CommanderViewClear);
				lookSlot.transform.position = eye;
				if (lookSlot.LinkedNightSight != null) lookSlot.LinkedNightSight.transform.position = eye;
			}
			return;
			var root = v.transform;
			var mount = LeclercSXXIMod.Rig?.Mounts.FirstOrDefault(m => m.Group == HeadGroup);
			var head = turret.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HeadGroup);
			if (mount == null || head == null)
			{
				MelonLogger.Warning($"{Tag}: no HL 120 head in the bundle; commander's sights left as they were.");
				return;
			}

			var slots = v.GetComponentsInChildren<CameraSlot>(true);
			var free = slots.FirstOrDefault(s => s.name == "commander head");
			var nods = free?.LinkedNightSight;
			if (free != null) free.transform.position = root.TransformPoint(CommanderHead);
			if (nods != null) nods.transform.position = root.TransformPoint(CommanderHead);
			var eyes = new GameObject("HL 120 optic frame").transform;
			eyes.SetParent(turret, false);
			eyes.SetPositionAndRotation(root.TransformPoint(CommanderEye), root.rotation);
			var window = new GameObject("HL 120 laser window").transform;
			window.SetParent(head, false);
			window.position = root.TransformPoint(CommanderEye);

			var weapons = v.GetComponent<WeaponsManager>().Weapons.Where(w => w.Role == WeaponSystemRole.MainGun || w.Role == WeaponSystemRole.Coaxial)
				.Select(w => w.Weapon).Where(w => w != null).ToArray();
			var holder = new GameObject("HL 120 setup");
			holder.SetActive(false);
			UsableOptic Copy(UsableOptic src, string name)
			{
				var go = UnityEngine.Object.Instantiate(src.gameObject, holder.transform, false);
				go.SetActive(false);
				go.name = name;
				DonorParts.Scrub(go, root);
				foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
				var o = go.GetComponent<UsableOptic>();
				o.FCS = st.Fcs;
				o.RegisterAtStart = false;
				o.Alignment = OpticAlignment.BoresightStabilized;
				o.RotateElevation = o.RotateAzimuth = true;
				o.CantCorrect = true;
				o.ForceHorizontalReticleAlign = true;
				o.LocalElevationLimits = CommanderPitch;
				o.LocalAzimuthLimits = Vector2.zero;
				o.OverrideRangeLimits = Vector2.zero;
				o.OverrideRangeStep = 0;
				o.Mounts = Array.Empty<AimablePlatform>();
				o.postAdditional = o.postAdditional?.Where(p => p != null).ToArray() ?? Array.Empty<PostProcessVolume>();
				o.transform.SetParent(eyes, false);
				o.transform.localPosition = Vector3.zero;
				o.transform.localRotation = Quaternion.identity;
				var s = o.slot;
				s.transform.SetPositionAndRotation(eyes.position, eyes.rotation);
				Reparent?.SetValue(s, s.transform == o.transform ? eyes : s.transform.parent);
				s.ExclusiveWeapons = weapons;
				s.ExclusiveItems = Array.Empty<GameObject>();
				s.ZeroScaleItems = Array.Empty<Transform>();
				s.SlotToUseOnDestroy = gunnerDay.slot;
				s.AllowFreeLook = false;
				s.IsExterior = false;
				s.DirectAttachOnly = false;
				s.LinkedNightSight = null;
				return o;
			}
			var day = Copy(gunnerNight, "HL 120 day");
			var night = Copy(gunnerNight, "HL 120 Iris thermal");
			var oldMarks = day.reticleMesh.gameObject;
			day.reticleMesh = UnityEngine.Object.Instantiate(gunnerDay.reticleMesh.gameObject, day.transform, false).GetComponent<ReticleMesh>();
			day.reticleMesh.transform.localPosition = Vector3.zero;
			UnityEngine.Object.DestroyImmediate(oldMarks);
			day.AltNightPost = null;
			day.slot.VisionType = NightVisionType.None;
			day.slot.BaseBlur = gunnerDay.slot.BaseBlur;
			day.slot.VibrationPreBlur = gunnerDay.slot.VibrationPreBlur;
			ConfigureDay(day, CommanderWide, CommanderNarrow, _dayMarks, copy: true);
			GunnerFcs.AimClamp.Register(st.Fcs, day);
			st.CommanderDay = day;
			if (ConfigureThermal(day, night, "HL 120", CommanderPitch, copy: true))
			{
				day.slot.LinkedNightSight = night.slot;
				st.CommanderThermal = night;
				night.gameObject.SetActive(true);
			}
			else UnityEngine.Object.Destroy(night.gameObject);
			day.gameObject.SetActive(true);
			UnityEngine.Object.Destroy(holder);
			GunnerFcs.HeadView(Tag, window, day.slot.transform, st.CommanderThermal?.slot.transform);
			GunnerFcs.LaserAim.FromWhile(st.Fcs, day.slot, window, FcsSettings.LaserMax);
			if (st.CommanderThermal != null) GunnerFcs.LaserAim.FromWhile(st.Fcs, night.slot, window, FcsSettings.LaserMax);
			var reference = st.Fcs.ReferenceTransform?.GetComponent<GunnerFcs.HeadPoint>();
			void SwitchReference(bool active)
			{
				if (reference == null) return;
				var point = active ? window : gunnerLaser;
				reference.Turret = point.parent;
				reference.Local = point.localPosition;
				reference.Put();
			}
			day.slot.ActiveChanged += SwitchReference;
			if (st.CommanderThermal != null) night.slot.ActiveChanged += SwitchReference;
			try { FitObserve(st, free, window, day, st.CommanderThermal); }
			catch (Exception e) { MelonLogger.Error($"{Tag}: HL 120 observation views failed: {e}"); }
			var link = TargetDesignation.Register(v, st.Fcs, new[] { free, nods, st.ObserveDay, st.ObserveThermal });
			CommanderCupola.Apply(v, new CommanderCupola.Settings
			{
Bone = head, Pivot = mount.Pivot, Axis = mount.Axis,
				Speed = 90f,
				Look = new[] { free, nods, day.slot, st.CommanderThermal?.slot, st.ObserveDay, st.ObserveThermal }.Where(s => s != null).ToArray(),
				Cu = link,
			});
			RemoteMg.LateSetup(v);
		}

		static void FitObserve(Fitted st, CameraSlot free, Transform window, UsableOptic day, UsableOptic night)
		{
			if (free == null) { MelonLogger.Warning($"{Tag}: no free-look commander view; no HL 120 observation views."); return; }
			CameraSlot Make(string name, CameraSlot like, float fov, float[] others)
			{
				var go = new GameObject(name) { layer = free.gameObject.layer };
				go.SetActive(false);
				go.transform.SetParent(window, false);
				var s = go.AddComponent<CameraSlot>();
				s.AllowFreeLook = true;
				s.AllowFreeZoom = false;
				s.DefaultFov = fov;
				s.OtherFovs = others ?? Array.Empty<float>();
				s.fovAspect = like.fovAspect;
				s.WrapFovLevels = false;
				s.BaseBlur = like.BaseBlur;
				s.VibrationBlurScale = like.VibrationBlurScale;
				s.VibrationPreBlur = like.VibrationPreBlur;
				s.VibrationShakeMultiplier = like.VibrationShakeMultiplier;
				s.SpriteType = like.SpriteType;
				s.ZeroScaleItems = Array.Empty<Transform>();
				s.ExclusiveItems = Array.Empty<GameObject>();
				s.ExclusiveWeapons = Array.Empty<WeaponSystem>();
				s.ZoomInAudioEvent = like.ZoomInAudioEvent;
				s.ZoomOutAudioEvent = like.ZoomOutAudioEvent;
				s.SlotToUseOnDestroy = free;
				s.IsExterior = false;
				return s;
			}
			var d = Make("HL 120 observe day", day.slot, CommanderWide, new[] { CommanderNarrow });
			Posts(d, day);
			if (night != null)
			{
				var ns = night.slot;
				var t = Make("HL 120 observe Iris thermal", ns, ns.DefaultFov, ns.OtherFovs);
				t.VisionType = NightVisionType.Thermal;
				t.OverrideFLIRResolution = true;
				t.FLIRWidth = ns.FLIRWidth;
				t.FLIRHeight = ns.FLIRHeight;
				t.FLIRFilterMode = ns.FLIRFilterMode;
				t.FLIRBlitMaterialOverride = ns.FLIRBlitMaterialOverride;
				t.CanToggleFlirPolarity = ns.CanToggleFlirPolarity;
				t.FLIRPolarityOnAudioEvent = ns.FLIRPolarityOnAudioEvent;
				t.FLIRPolarityOffAudioEvent = ns.FLIRPolarityOffAudioEvent;
				t.SlotToUseOnDestroy = free;
				Posts(t, night);
				t.gameObject.AddComponent<ThermalSight.Resolution>().Init(t, ThermalZoom, ThermalWidth, ThermalHeight);
				d.LinkedNightSight = t;
				d.NightSightAtNightOnly = false;
				t.gameObject.SetActive(true);
				st.ObserveThermal = t;
			}
			d.gameObject.SetActive(true);
			st.ObserveDay = d;
		}

		static void Posts(CameraSlot s, UsableOptic from)
		{
			var vols = new List<PostProcessVolume>();
			foreach (var p in new[] { from.post }.Concat(from.postAdditional ?? Array.Empty<PostProcessVolume>()))
			{
				if (p == null) continue;
				var go = new GameObject(p.name) { layer = p.gameObject.layer };
				go.transform.SetParent(s.transform, false);
				var v = go.AddComponent<PostProcessVolume>();
				v.isGlobal = true;
				v.weight = p.weight;
				v.priority = p.priority;
				v.sharedProfile = p.sharedProfile;
				v.enabled = false;
				vols.Add(v);
			}
			s.ActiveChanged += on => { foreach (var v in vols) if (v != null) v.enabled = on; };
		}
	}
}
