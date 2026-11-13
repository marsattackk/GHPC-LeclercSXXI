using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Player;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class RemoteMg
	{
		static readonly FieldInfo StageDuration = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		static readonly FieldInfo UnitSlots = Reflect.Field(typeof(Unit), "_designatedCameraSlots");
		internal static readonly FieldInfo WeaponCycle = Reflect.Field(typeof(WeaponSystem), "_cycleTimeSeconds");

		internal abstract class Station : MonoBehaviour
		{
			internal WeaponSystemInfo Info;
			internal abstract IEnumerable<CameraSlot> Sights { get; }

			internal virtual IEnumerable<CameraSlot> WeaponSights => Sights;
			internal virtual bool FromHeadView => false;
			internal bool IsWeaponSight(CameraSlot s) => s != null && WeaponSights.Any(w => w != null && (w == s || (s.IsLinkedNightSight && s.LinkedDaySight == w)));
		}

		static readonly Dictionary<WeaponSystem, Station> ByWeapon = new Dictionary<WeaponSystem, Station>();
		internal static Station Of(WeaponSystem w)
		{
			if (w == null) return null;
			if (ByWeapon.TryGetValue(w, out var st) && (st == null || st != null && st.Info?.Weapon == w)) return st;
			foreach (var dead in ByWeapon.Keys.Where(k => k == null).ToList()) ByWeapon.Remove(dead);
			var v = w.GetComponentInParent<Vehicle>();
			st = v != null ? v.GetComponentsInChildren<Station>(true).FirstOrDefault(x => x.Info?.Weapon == w) : null;
			ByWeapon[w] = st;
			return st;
		}

		// In a free-look slot GHPC aims the selected weapon at the cursor and fires it. The station is remote.
		internal static bool InPlayerInput;

		[HarmonyPatch(typeof(PlayerInput), "Update")]
		static class PlayerInputScope
		{
			static void Prefix() => InPlayerInput = true;
			static Exception Finalizer(Exception __exception) { InPlayerInput = false; return __exception; }
		}

		[HarmonyPatch]
		static class PlayerControlPatch
		{
			static MethodBase[] _targets;
			static MethodBase[] Targets()
			{
				if (_targets != null) return _targets;
				var t = typeof(CrewBrainWeaponsModule);
				return _targets = new MethodBase[]
				{
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.Fire), new[] { typeof(WeaponSystemRole), typeof(IUnit) }),
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.AdjustAimByRatio), new[] { typeof(WeaponSystemRole), typeof(float), typeof(float) }),
					Reflect.Method(t, nameof(CrewBrainWeaponsModule.SetAimVector), new[] { typeof(WeaponSystemRole), typeof(Vector3), typeof(bool) }),
				}.Where(m => m != null).ToArray();
			}

			static bool Prepare() => Targets().Length > 0;
			static IEnumerable<MethodBase> TargetMethods() => Targets();

			static bool Prefix(CrewBrainWeaponsModule __instance, WeaponSystemRole __0)
			{
				var weaponSystemRole = __0;
				if (!InPlayerInput || weaponSystemRole != WeaponSystemRole.RoofGun) return true;
				var slot = CameraSlot.ActiveInstance;
				if (slot == null || !slot.AllowFreeLook) return true;
				var input = PlayerInput.Instance;
				if (input == null || (object)input.CurrentPlayerCrewBrain?.WeaponsModule != __instance) return true;
				var w = __instance.ActiveWeapon;
				if (w == null || w.Role != weaponSystemRole) return true;
				var st = Of(w.Weapon);
				return st == null || st.FromHeadView || st.IsWeaponSight(slot);
			}
		}

		internal static float StagesSum(IEnumerable<AmmoFeed.ReloadStage> stages) => stages.Sum(x => (float)StageDuration.GetValue(x));

		internal static void SetRate(WeaponSystem w, float roundsPerMinute)
		{
			float cycle = 60f / roundsPerMinute, now = StagesSum(w.Feed.RoundCycleStages);
			if (now > 0f) foreach (var x in w.Feed.RoundCycleStages) StageDuration.SetValue(x, (float)StageDuration.GetValue(x) * cycle / now);
			w.SetCycleTime(cycle);
		}

		internal static void LateSetup(Vehicle vehicle)
		{
			foreach (var st in vehicle.GetComponentsInChildren<Station>(true)) LateSetup(vehicle, st);
		}

		static void LateSetup(Vehicle vehicle, Station st)
		{
			var slots = vehicle.DesignatedCameraSlots;
			var missing = st.Sights.Where(s => s != null && (slots == null || !slots.Contains(s))).ToArray();
			if (slots != null && slots.Length > 0 && missing.Length > 0) UnitSlots.SetValue(vehicle, slots.Concat(missing).ToArray());
			var crew = vehicle.GetComponent<CrewManager>();
			if (crew?.CrewMembers == null || st.Info == null) return;
			foreach (var m in crew.CrewMembers)
			{
				var module = m?.Brain?.WeaponsModule;
				if (module?.Weapons == null || m.Position != CrewPosition.Gunner) continue;
				if (!module.Weapons.Contains(st.Info)) module.Weapons.Add(st.Info);
			}
		}

		// Every path to the camera manager ends here, and the slots it gets don't have the station's sights.
		[HarmonyPatch(typeof(CameraManager), "RescanCamSlots")]
		static class CameraSlotsPatch
		{
			static void Prefix(ref CameraSlot[] __0)
			{
				try { __0 = WithStation(__0); }
				catch (Exception e) { PatchGuard.Failed("Station sights for the camera", e); }
			}

			static CameraSlot[] WithStation(CameraSlot[] cameraSlots)
			{
				if (cameraSlots == null || cameraSlots.Length == 0) return cameraSlots;
				var unit = cameraSlots.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
				var stations = unit != null ? unit.GetComponentsInChildren<Station>(true) : new Station[0];
				if (stations.Length == 0) return cameraSlots;
				var given = cameraSlots;
				var missing = stations.SelectMany(st => st.Sights).Where(s => s != null && !given.Contains(s)).Distinct().ToArray();
				if (missing.Length == 0) return cameraSlots;
				return cameraSlots.Concat(missing).ToArray();
			}
		}

		// Shift goes back in by a stored index into a list that shrinks when the Kord isn't selected. Out of range it throws
		// and leaves the view stuck outside.
		static CameraSlot _lastSight;
		internal static CameraSlot LastSight
		{
			get { if (!ReferenceEquals(_lastSight, null) && _lastSight == null) _lastSight = null; return _lastSight; }
			private set => _lastSight = value;
		}

		static readonly FieldInfo FixedSlotsField = Reflect.Field(typeof(CameraManager), "_allFixedCamSlots"),
			FreeSlotsField = Reflect.Field(typeof(CameraManager), "_allFreeLookCamSlots"),
			ValidSlotsField = Reflect.Field(typeof(CameraManager), "_validCamSlots"),
			InteriorIndexField = Reflect.Field(typeof(CameraManager), "_interiorSlotIndex"),
			ExteriorIndexField = Reflect.Field(typeof(CameraManager), "_exteriorSlotIndex");

		static bool HasStation(IEnumerable<CameraSlot> slots)
		{
			var unit = slots?.FirstOrDefault(s => s != null)?.GetComponentInParent<Vehicle>();
			return unit != null && unit.GetComponentInChildren<Station>(true) != null;
		}

		[HarmonyPatch(typeof(CameraManager), "AttachCamToSlotDirect")]
		static class RememberSightPatch
		{
			static void Postfix(CameraSlot __0)
			{
				var camSlot = __0;
				if (camSlot != null && !camSlot.AllowFreeLook) LastSight = camSlot.IsLinkedNightSight && camSlot.LinkedDaySight != null ? camSlot.LinkedDaySight : camSlot;
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ToggleCameraSlotSet")]
		static class SlotSetPatch
		{
			static void Prefix(CameraManager __instance)
			{
				bool goingIn = __instance.ExteriorMode;
				var all = (CameraSlot[])(goingIn ? FixedSlotsField : FreeSlotsField).GetValue(__instance);
				if (all == null || !HasStation(all)) return;
				var usable = all.Where(s => s != null && s.IsAvailable).ToList();
				if (usable.Count == 0) return;
				var field = goingIn ? InteriorIndexField : ExteriorIndexField;
				int old = (int)field.GetValue(__instance);
				int i;
				if (goingIn) { i = LastSight != null ? usable.IndexOf(LastSight) : -1; if (i < 0) i = 0; }
				else i = old >= 0 && old < usable.Count ? old : 0;
				if (i == old) return;
				field.SetValue(__instance, i);
			}
		}

		[HarmonyPatch(typeof(CameraManager), "ResetTempAttach")]
		static class TempAttachPatch
		{
			static void Prefix(CameraManager __instance)
			{
				var valid = (List<CameraSlot>)ValidSlotsField.GetValue(__instance);
				if (valid == null || valid.Count == 0) return;
				var field = __instance.ExteriorMode ? ExteriorIndexField : InteriorIndexField;
				int old = (int)field.GetValue(__instance);
				if (old >= 0 && old < valid.Count) return;
				if (!HasStation(valid)) return;
				int i = LastSight != null ? valid.IndexOf(LastSight) : -1;
				field.SetValue(__instance, i < 0 ? 0 : i);
			}
		}
	}
}
