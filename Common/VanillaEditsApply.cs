using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Utility;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class VanillaEditsApply
	{
		static readonly FieldInfo RestRotation = Reflect.Field(typeof(AimablePlatform), "_originalLocalRotation");
		static readonly FieldInfo GroupColliders = Reflect.Field(typeof(RaycastColliderGroup), "childColliders");

		internal static string Apply(Transform root, VanillaEdits e, IDictionary<string, (Mesh mesh, Transform bone)> reshape, List<string> problems = null)
		{
			// Through the groups' stored offsets with turret and gun at rest, the groups lag and the AI may be traversing.
			var rest = new Dictionary<Transform, Quaternion>();
			foreach (var p in root.GetComponentsInChildren<AimablePlatform>(true))
				if (p.Transform != null) rest[p.Transform] = (Quaternion)RestRotation.GetValue(p);
			var groups = Groups(root);
			var log = new List<string>();
			void Problem(string s) { log.Add(s); problems?.Add(s); }
			List<Transform> Find(string name)
			{
				var found = groups.SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Select(c => c.transform)
					.Where(t => t.name == name).Distinct().ToList();
				if (found.Count == 0) Problem($"'{name}' not found");
				return found;
			}

			foreach (var n in e.Off)
				foreach (var t in Find(n)) { t.gameObject.SetActive(false); log.Add($"off {n}"); }

			foreach (var (n, m) in e.Transforms)
				foreach (var t in Find(n))
				{
					var f = Follower(t);
					if (f == null) { Problem($"'{n}': no LateFollow above it, not moved"); continue; }
					var boneRest = RestInRoot(root, f.FollowTarget, rest);
					var inBone = boneRest.inverse * m * boneRest;
					var before = InBone(f, t);
					SetLocal(t, InBone(f, t.parent).inverse * inBone * before);
					log.Add($"moved {n} by {Shift(before, InBone(f, t), boneRest)} cm");
				}

			foreach (var n in e.Reshape)
			{
				if (!reshape.TryGetValue(n, out var r)) { Problem($"'{n}': no reshape piece in the bundle, not reshaped"); continue; }
				if (!r.mesh.isReadable) { Problem($"'{n}': reshape mesh not readable, not reshaped"); continue; }
				foreach (var t in Find(n))
				{
					var f = Follower(t);
					var mc = t.GetComponent<MeshCollider>();
					if (f == null || mc == null) { Problem($"'{n}': {(f == null ? "no LateFollow above it" : "no MeshCollider")}, not reshaped"); continue; }
					var pose = RestInRoot(root, f.FollowTarget, rest).inverse * RestInRoot(root, r.bone, rest);
					var kids = t.Cast<Transform>().Where(k => k.name != "inverted armor clone").Select(k => (k, f.transform.worldToLocalMatrix * k.localToWorldMatrix)).ToList();
					SetLocal(t, InBone(f, t.parent).inverse * pose);
					t.localScale = Vector3.one;
					foreach (var (k, inGroup) in kids)
					{
						var w = f.transform.localToWorldMatrix * inGroup;
						k.SetPositionAndRotation(w.GetColumn(3), w.rotation);
					}
					bool wasConvex = mc.convex;
					mc.convex = false;
					mc.sharedMesh = r.mesh;
					var mf = t.GetComponent<MeshFilter>();
					if (mf != null) mf.sharedMesh = r.mesh;
					string clone = "";
					// VariableArmor measures the exit through this clone, built from the old mesh in Awake.
					var c = t.GetComponent<VariableArmor>() != null ? t.Find("inverted armor clone") : null;
					if (c != null)
					{
						var inv = Inverted(r.mesh);
						var cf = c.GetComponent<MeshFilter>(); if (cf != null) cf.sharedMesh = inv;
						var cc = c.GetComponent<MeshCollider>(); if (cc != null) cc.sharedMesh = inv;
						clone = $", inverted clone replaced (filter {(cf != null ? "ok" : "MISSING")}, collider {(cc != null ? "ok" : "MISSING")})";
					}
					else if (t.GetComponent<VariableArmor>() != null) clone = ", VariableArmor WITHOUT its inverted clone";
					log.Add($"reshaped {n}: {r.mesh.triangles.Length / 3} faces{(wasConvex ? " (was convex)" : "")}{clone}{(kids.Count > 0 ? $", {kids.Count} children kept in place" : "")}");
				}
			}

			foreach (var (path, group, delta) in e.Bones)
			{
				var bone = root.Find(path);
				if (bone == null) { Problem($"bone '{path}' not found"); continue; }
				var local = RestInRoot(root, bone.parent, rest).inverse.MultiplyVector(delta);
				bone.localPosition += local;
				var fs = Followers.On(root, bone);
				foreach (var f in fs) f.transform.SetPositionAndRotation(bone.TransformPoint(Followers.PosShift(f)), bone.rotation * Followers.RotShift(f));
				bool groupOn = fs.Any(f => f.name.Contains(group));
				log.Add($"bone {path} moved by ({delta.x * 100f:0.0}, {delta.y * 100f:0.0}, {delta.z * 100f:0.0}) cm, {fs.Count} followers re-synced" +
					(groupOn ? $" ({group} among them)" : $"; '{group}' does NOT follow it"));
			}

			// Last, the edits above find pieces by their collider.
			foreach (var n in e.Strip)
				foreach (var t in Find(n))
				{
					var cols = t.GetComponents<Collider>();
					foreach (var g in groups.SelectMany(x => x.GetComponentsInChildren<RaycastColliderGroup>(true)).Distinct())
						if (GroupColliders.GetValue(g) is List<Collider> list) foreach (var col in cols) list.Remove(col);
					int armour = 0;
					foreach (var a in t.GetComponents<IArmor>()) { Object.Destroy(a as Component); armour++; }
					foreach (var col in cols) Object.Destroy(col);
					foreach (var r in t.GetComponents<Renderer>()) Object.Destroy(r);
					log.Add($"stripped {n} ({cols.Length} colliders, {armour} armour; {t.childCount} children kept)");
				}

			return string.Join("; ", log);
		}

		static List<Transform> Groups(Transform root)
		{
			var set = new HashSet<LateFollow>(root.GetComponentsInChildren<LateFollow>(true));
			foreach (var t in root.GetComponentsInChildren<LateFollowTarget>(true))
				foreach (var f in t.LateFollowers) if (f != null) set.Add(f);
			return set.Where(f => RigBones.IsColliderGroup(f.name)).Select(f => f.transform).ToList();
		}

		static LateFollow Follower(Transform t) => t.GetComponentsInParent<LateFollow>(true).FirstOrDefault(f => f.FollowTarget != null);

		static Matrix4x4 InBone(LateFollow f, Transform t) =>
			Matrix4x4.TRS(Followers.PosShift(f), Followers.RotShift(f), Vector3.one) * (f.transform.worldToLocalMatrix * t.localToWorldMatrix);

		static Matrix4x4 RestInRoot(Transform root, Transform t, Dictionary<Transform, Quaternion> rest)
		{
			var m = Matrix4x4.identity;
			for (var x = t; x != null && x != root; x = x.parent)
				m = Matrix4x4.TRS(x.localPosition, rest.TryGetValue(x, out var r) ? r : x.localRotation, x.localScale) * m;
			return m;
		}

		static void SetLocal(Transform t, Matrix4x4 local)
		{
			t.localPosition = local.GetColumn(3);
			t.localRotation = local.rotation;
		}

		static string Shift(Matrix4x4 before, Matrix4x4 after, Matrix4x4 boneRest)
		{
			var d = boneRest.MultiplyVector((Vector3)after.GetColumn(3) - (Vector3)before.GetColumn(3)) * 100f;
			return $"({d.x:0.0}, {d.y:0.0}, {d.z:0.0})";
		}

		static Mesh Inverted(Mesh m)
		{
			var inv = new Mesh { name = m.name + " (inverted)", indexFormat = m.indexFormat };
			inv.SetVertices(m.vertices);
			var t = m.triangles;
			for (int i = 0; i < t.Length; i += 3) { int a = t[i]; t[i] = t[i + 1]; t[i + 1] = a; }
			inv.SetTriangles(t, 0);
			inv.RecalculateNormals(); inv.RecalculateBounds();
			return inv;
		}
	}
}
