using System.Collections.Generic;
using System.Linq;
using GHPC.Effects;
using GHPC.Vehicle;
using HarmonyLib;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class BlowoutRack
	{
		internal sealed class Lid
		{
			public string Name;
			public string ExitName;
			public float ExitRadius = 0.4f;
			public FlammableSourceType Source = FlammableSourceType.SideLarge;
			public float MassKg = 50f;
			public float FlameHeightTrigger = 1f;
			public float ForceForFlameTrigger = 700f;
		}

		internal sealed class Settings
		{
			public string Prefix = "";
			public string RackPath;
			public string RackName;
			public float RetrievalDelay = 4f, StorageDelay = 2f;
			public string CompartmentName;
			public string VolumeName;
			public string UpReference;
			public Lid[] Lids = new Lid[0];
			public float ResidualTnt = 1f, ResidualFlame = 2f, ResidualIgniteMin = 160f, ResidualIgniteMax = 170f, ResidualBurn = 180f, ResidualTemp = 600f;
			public string ReplaceCompartment;
		}

		// GHPC kills chassis power on a burst in any compartment. The box is sealed off from the crew and engine, so the tank keeps running.
		internal sealed class Isolated : MonoBehaviour { public GHPC.Unit Unit; }

		static readonly AccessTools.FieldRef<CompartmentExit, Compartment> ExitCompartment = Reflect.FieldRefAccess<CompartmentExit, Compartment>("_compartment");

		[HarmonyPatch(typeof(CompartmentExit), nameof(CompartmentExit.SetFireRatioByFlameHeight))]
		static class FireScalePatch
		{
			// The drawn flames only, the height that throws the lids and heats the box stays GHPC's.
			static void Prefix(CompartmentExit __instance, ref float __0)
			{
				if (__instance != null) ExitCompartment(__instance);
			}
		}

		// GHPC's cook-off blast is a sphere that hits its own tank through the floor, it took the engine out 1.4 m below.
		// Own parts hidden while the box updates, so it goes up and out like the panels are meant to send it.
		[HarmonyPatch(typeof(Compartment), nameof(Compartment.DoUpdate))]
		static class VentPatch
		{
			static readonly Stack<List<GHPC.Equipment.DestructibleComponent>> Hidden = new Stack<List<GHPC.Equipment.DestructibleComponent>>();

			static void Prefix(Compartment __instance)
			{
				var iso = __instance != null ? __instance.GetComponent<Isolated>() : null;
				var items = iso?.Unit?.InfoBroker?.BlastDamageRouter?.BlastableItems;
				var mine = items != null ? new List<GHPC.Equipment.DestructibleComponent>() : null;
				Hidden.Push(mine);
				if (items == null) return;
				foreach (var b in items)
					if (b.Component != null && !b.Component.Hidden) { b.Component.Hidden = true; mine.Add(b.Component); }
			}

			static void Finalizer()
			{
				var mine = Hidden.Count > 0 ? Hidden.Pop() : null;
				if (mine != null) foreach (var c in mine) if (c != null) c.Hidden = false;
			}
		}

		static readonly AccessTools.FieldRef<Compartment, FlammablesManager> CompartmentManager = Reflect.FieldRefAccess<Compartment, FlammablesManager>("_manager");
		static readonly System.Reflection.FieldInfo DidBurstField = Reflect.Field(typeof(FlammablesManager), "DidBurst");

		[HarmonyPatch(typeof(Compartment), "DoBurst")]
		static class BurstPatch
		{
			static bool Prefix(Compartment __instance, float __0)
			{
				float ratio = __0;
				if (__instance == null || __instance.Parent != null || __instance.GetComponent<Isolated>() == null) return true;
				foreach (var exit in __instance.Exits) exit.MakeFireBurst(ratio);
				var fm = CompartmentManager(__instance);
				if (fm != null) (DidBurstField?.GetValue(fm) as System.Action<float>)?.Invoke(ratio);
				return false;
			}
		}

		static readonly AccessTools.FieldRef<GHPC.Weapons.AmmoRack, Compartment> RackCompartment = Reflect.FieldRefAccess<GHPC.Weapons.AmmoRack, Compartment>("_compartment");
		static readonly AccessTools.FieldRef<GHPC.Weapons.AmmoRack, float> RackRetrieval = Reflect.FieldRefAccess<GHPC.Weapons.AmmoRack, float>("_retrievalDelaySeconds");
		static readonly AccessTools.FieldRef<GHPC.Weapons.AmmoRack, float> RackStorage = Reflect.FieldRefAccess<GHPC.Weapons.AmmoRack, float>("_storageDelaySeconds");
		static readonly AccessTools.FieldRef<FlammablesCluster, Transform> ClusterMarker = Reflect.FieldRefAccess<FlammablesCluster, Transform>("_fireMarker");

		internal static void Apply(Vehicle vehicle, Settings s, Mesh volume, Transform bone, List<string> problems)
		{
			var root = vehicle.transform;
			var rackT = InteriorMap.Find(root, s.RackPath);
			var rack = rackT != null ? rackT.GetComponent<GHPC.Weapons.AmmoRack>() : null;
			var fm = vehicle.GetComponent<FlammablesManager>();
			if (rack == null) { problems.Add($"rack '{s.RackPath}' not found"); return; }
			if (fm == null || fm.Compartments == null || fm.Compartments.Length == 0) { problems.Add("no FlammablesManager compartments"); return; }
			Compartment replaced = null;
			if (!string.IsNullOrEmpty(s.ReplaceCompartment))
			{
				if (fm.Compartments.Any(c => c != null && c.Name == s.CompartmentName && c.GetComponent<Isolated>() != null)) return;
				replaced = fm.Compartments.FirstOrDefault(c => c != null && c.Name == s.ReplaceCompartment);
				var rmc = replaced != null ? replaced.GetComponent<MeshCollider>() : null;
				if (rmc == null || rmc.sharedMesh == null) { problems.Add($"compartment '{s.ReplaceCompartment}' to replace not found"); return; }
				volume = rmc.sharedMesh;
			}
			if (volume == null || (bone == null && replaced == null)) { problems.Add($"no volume mesh '{s.VolumeName}' in the bundle"); return; }
			var sibling = replaced != null ? replaced.GetComponent<Collider>() : fm.Compartments.Select(c => c != null ? c.GetComponent<Collider>() : null)
				.FirstOrDefault(c => c != null && c.GetComponentsInParent<GHPC.Utility.LateFollow>(true).Any(f => f.FollowTarget == bone));
			if (sibling == null) { problems.Add($"no compartment volume following '{bone.name}' to put the box beside"); return; }

			// Built inactive, Compartment.Awake has to see every field set.
			var go = new GameObject(s.VolumeName);
			go.SetActive(false);
			if (replaced != null)
			{
				go.transform.SetParent(replaced.transform.parent, false);
				go.transform.localPosition = replaced.transform.localPosition; go.transform.localRotation = replaced.transform.localRotation;
				go.transform.localScale = replaced.transform.localScale;
				if (replaced.GetComponent<DmArmourApply.ImportedCompartment>() != null) go.AddComponent<DmArmourApply.ImportedCompartment>();
				fm.Compartments = fm.Compartments.Where(c => c != replaced).ToArray();
				foreach (var cl in replaced.Clusters ?? new List<FlammablesCluster>()) if (cl != null && cl != rack.Flammables) problems.Add($"compartment '{replaced.Name}' held cluster '{cl.Name}': dropped with it");
			}
			else Followers.PlaceOnBone(go.transform, bone, sibling.transform.parent);
			go.layer = sibling.gameObject.layer;
			go.tag = sibling.gameObject.tag;
			if (replaced != null) Object.DestroyImmediate(replaced.gameObject);
			var mc = go.AddComponent<MeshCollider>();
			mc.sharedMesh = volume;
			mc.convex = false;

			var res = new GameObject("residual burning");
			res.SetActive(false);
			res.transform.SetParent(go.transform, false);
			res.transform.localPosition = volume.bounds.center;
			res.layer = go.layer;
			var item = res.AddComponent<FlammableItem>();
			Traverse.Create(item).Field("_explosive").SetValue(false);
			Traverse.Create(item).Field("_tntEquivalent").SetValue(s.ResidualTnt);
			Traverse.Create(item).Field("_selfOxidizing").SetValue(false);
			Traverse.Create(item).Field("_flameHeightMeters").SetValue(s.ResidualFlame);
			Traverse.Create(item).Field("_ignitionTempMin").SetValue(s.ResidualIgniteMin);
			Traverse.Create(item).Field("_ignitionTempMax").SetValue(s.ResidualIgniteMax);
			Traverse.Create(item).Field("_shortestBurnTime").SetValue(s.ResidualBurn);
			Traverse.Create(item).Field("_burnTemperature").SetValue(s.ResidualTemp);
			item.HeatTransferRatio = 1f;
			res.SetActive(true);
			var residual = new FlammablesCluster { Name = "residual burn", MaxTempC = 1000f };
			residual.Items.Add(item);
			ClusterMarker(residual) = res.transform;

			var old = RackCompartment(rack);
			if (old != null)
			{
				old.Clusters.Remove(rack.Flammables);
				old.Parent?.Clusters.Remove(rack.Flammables);   // a child compartment's clusters are listed in its parent too
			}

			var up = InteriorMap.Find(root, s.UpReference) ?? bone;
			var body = vehicle.GetComponent<Rigidbody>();
			var exits = new List<CompartmentExit>();
			var detach = new List<DetachableParent>();
			var all = root.GetComponentsInChildren<Transform>(true);
			foreach (var l in s.Lids)
			{
				var lid = all.FirstOrDefault(t => t.name == s.Prefix + l.Name) ?? all.FirstOrDefault(t => t.name == l.Name);
				var mf = lid != null ? lid.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.sharedMesh != null) : null;
				if (lid == null || mf == null) { problems.Add($"lid '{l.Name}' not found; the box vents without it"); continue; }
				var b = mf.sharedMesh.bounds;
				var colGo = new GameObject("blow-off collider");
				colGo.SetActive(false);
				colGo.layer = 14;
				colGo.transform.SetParent(mf.transform, false);
				var box = colGo.AddComponent<BoxCollider>();
				box.center = b.center; box.size = b.size;
				var dpGo = new GameObject(l.ExitName + " blow-off");
				dpGo.SetActive(false);
				dpGo.transform.SetParent(go.transform, false);
				var dp = dpGo.AddComponent<DetachableParent>();
				dp.Transform = lid;
				dp.UpReferenceTransform = up;
				dp.MassKg = l.MassKg;
				dp.OverpressureTrigger = 0f;
				dp.OverpressureToForceFactor = 1f;
				dp.FlameHeightTrigger = l.FlameHeightTrigger;
				dp.ForceForFlameTrigger = l.ForceForFlameTrigger;
				dp.MassDecreaseItem = body;
				dp.MaxTorque = 500f; dp.MinLateralForce = 300f; dp.MaxLateralForce = 600f;
				dp.DownforceMultiplier = 0f;
				dp.LocalShiftOnDetach = new Vector3(0f, 0.07f, 0f);
				dp.ColliderObjects = new[] { colGo };
				dp.ObjectsToDeactivate = new GameObject[0];
				dp.TransformsToLeaveBehind = new Transform[0];
				dp.AarHideItemsParents = new Transform[0];
				dp.OtherParents = new Transform[0];
				dpGo.SetActive(true);
				var marker = new GameObject(l.ExitName + " marker").transform;
				marker.SetParent(go.transform, false);
				marker.position = mf.transform.TransformPoint(b.center);
				marker.rotation = Quaternion.LookRotation(up.up, -up.forward);
				exits.Add(new CompartmentExit
				{
					Name = l.ExitName, Radius = l.ExitRadius, FlameHeightThreshold = 0f, SourceType = l.Source, PositionMarker = marker,
					AnimatedParts = new AnimatedPart[0], DetachTrigger = dp, KillFireWhenDetached = false, Closed = true, PressureToOpenBar = 0f,
				});
				detach.Add(dp);
			}

			var iso = go.AddComponent<Isolated>();
			iso.Unit = vehicle;
			var comp = go.AddComponent<Compartment>();
			comp.Name = s.CompartmentName;
			comp.Parent = null;
			comp.Volumes = new Collider[] { mc };
			comp.TotalVolume = 1f;
			comp.Exits = exits;
			comp.Detachables = detach;
			comp.UnsecuredFiresHere = false;
			comp.Clusters = new List<FlammablesCluster> { residual, rack.Flammables };
			go.SetActive(true);
			comp.RegisterFlammablesManager(fm);
			fm.Compartments = fm.Compartments.Concat(new[] { comp }).ToArray();

			RackCompartment(rack) = comp;
			RackRetrieval(rack) = s.RetrievalDelay;
			RackStorage(rack) = s.StorageDelay;
			if (!string.IsNullOrEmpty(s.RackName)) rack.Name = s.RackName;
		}
	}
}
