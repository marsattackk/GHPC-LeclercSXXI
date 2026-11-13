using System.Linq;
using UnityEngine;

namespace LeclercSXXI.Common
{
	public static class BaseRigT80B
	{
		public const string HullPath = "T80B_rig/HULL", TurretPath = "T80B_rig/HULL/TURRET", GunPath = "T80B_rig/HULL/TURRET/gun";

		public static readonly Vector3 TurretPivot = new Vector3(0f, 1.412f, -1.1715f), GunPivot = new Vector3(0f, 1.6518f, -0.1493f);

		public static readonly string[] HiddenRenderers =
		{
			"body", "turret", "turret_skin", "skirts", "driver hatch mesh", "headlight_glass", "NSV", "nsv mount",
			"12.7_1", "12.7_2", "12.7_3", "camo_rear", "camo_side", "hose", "log", "sidepack", "suitcase_3stack_a", "towropes_front",
			"net_barrel", "net_glacis", "net_turret",
			"tac marker", "turret numbers",
			"Luna visible glow", "Luna lens glow",
			"lp_R123",
		};

		public static readonly string[] LampRenderers = { "gs_turret", "gst_illum", "march light", "march light non illum" };

		public static readonly string[] GearRenderers = { "wheels", "track_l", "track_r" };

		public static void HideRunningGear(Transform root, string ownPrefix)
		{
			KeepHidden.HideGear(root, GearRenderers, ownPrefix);
		}

		public static void HideBaseVisuals(Transform root, string ownPrefix, bool keepLamps = false)
		{
			bool UnderOurs(Transform t) { for (var x = t; x != null && x != root; x = x.parent) if (x.name.StartsWith(ownPrefix)) return true; return false; }
			var names = keepLamps ? HiddenRenderers : HiddenRenderers.Concat(LampRenderers).ToArray();
			var rs = root.GetComponentsInChildren<Renderer>(true)
				.Where(r => !(r is ParticleSystemRenderer) && names.Contains(r.gameObject.name) && !UnderOurs(r.transform))
				.ToArray();
			foreach (var r in rs) r.enabled = false;
			KeepHidden.Add(root, rs, null);
		}

		public static Material FindTemplate(Transform root)
		{
			foreach (var r in root.GetComponentsInChildren<Renderer>(true))
				if (r.name == "body" || r.name == "turret")
				{
					var m = r.sharedMaterials.FirstOrDefault(x => x != null && x.shader.name == "GHPC/VehicleShader");
					if (m != null) return m;
				}
			return null;
		}
	}
}
