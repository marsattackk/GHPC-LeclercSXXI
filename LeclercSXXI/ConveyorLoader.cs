using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FMOD.Studio;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	[DefaultExecutionOrder(10000)]
	internal sealed class ConveyorLoader : MonoBehaviour
	{
		static readonly FieldInfo Clock = Reflect.Field(typeof(AmmoFeed), "_clipFeedTime");
		static readonly FieldInfo Duration = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		sealed class Cell
		{
			internal int SlotIndex, Rank;
			internal Transform Slot, Carrier;
			internal Vector3 Home;
		}

		AmmoFeed _feed;
		Transform _turret, _root, _door, _block, _gutter;
		readonly List<Cell> _cells = new List<Cell>();
		readonly List<Transform> _rammer = new List<Transform>();
		Vector3[] _nodes;
		Vector3 _doorRest, _blockRest, _blockTravel, _loading, _direction;
		float _phase, _startPhase, _goalPhase, _stroke;
		int _loadingRank, _target = -1;
		bool _running;
		Transform _round;
		Vector3 _roundRest;
		Material _metal, _light;
		readonly List<Mesh> _ownedMeshes = new List<Mesh>();
		readonly List<Transform> _parts = new List<Transform>();
		AarVisual _sourceBreech;
		static Mesh _cellMesh, _box;

		internal static void Configure(Vehicle vehicle)
		{
			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0]?.Weapon?.Feed;
			if (feed == null) return;
			float[] seconds = { 2f, 0.64f, 1.2f, 0.96f, 0.6f, 0.48f, 0.12f };
			feed.ClipReloadStages = seconds.Select((s, index) =>
			{
				var stage = new AmmoFeed.ReloadStage {
					AnimatedParts = new AnimatedPart[0], StageClips = new AudioClip[0],
					ClipReloadFMODParameters = new[] { new AmmoFeed.ReloadStage.FMODParameter { Name = "WaitForCarousel", Value = index == 0 ? 1f : 0f } },
					LiveDuration = s,
				};
				Duration.SetValue(stage, s);
				return stage;
			}).ToArray();
		}

		internal static void Attach(Vehicle vehicle)
		{
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons[0];
			var feed = main?.Weapon?.Feed;
			if (feed == null || feed.GetComponent<ConveyorLoader>() != null) return;
			var rack = feed.ReadyRack;
			var turret = main.FCS?.TurretPlatform?.Transform;
			if (turret == null || rack?.VisualSlots?.Count != LoaderMotion.Capacity || feed.Carousel == null || Clock == null)
			{
				MelonLogger.Warning("Leclerc SXXI: conveyor animation could not find its 22 cells and gun feed.");
				return;
			}
			var loader = feed.gameObject.AddComponent<ConveyorLoader>();
			loader._feed = feed; loader._turret = turret;
			try { loader.Build(vehicle); }
			catch (Exception e)
			{
				loader.enabled = false;
				if (loader._sourceBreech != null) loader._sourceBreech.Hidden = false;
				foreach (var part in loader._parts) if (part != null) part.gameObject.SetActive(false);
				MelonLogger.Warning("Leclerc SXXI: conveyor animation could not be fitted: " + e.Message);
				return;
			}
			feed.ReloadStageStarted += loader.Stage;
			feed.gameObject.AddComponent<StorageLoadSound>().Watch(feed);
		}

		void Build(Vehicle vehicle)
		{
			_root = new GameObject("LeclercSXXI conveyor animation").transform;
			_root.SetParent(_turret, false);
			for (int i = 0; i < _feed.ReadyRack.VisualSlots.Count; i++)
			{
				var t = _feed.ReadyRack.VisualSlots[i];
				_cells.Add(new Cell { SlotIndex = i, Slot = t, Home = _turret.InverseTransformPoint(t.position) });
			}
			float midY = _cells.Average(c => c.Home.y);
			var ordered = _cells.Where(c => c.Home.y > midY).OrderByDescending(c => c.Home.x)
				.Concat(_cells.Where(c => c.Home.y <= midY).OrderBy(c => c.Home.x)).ToArray();
			if (ordered.Take(11).Any(c => c.Home.y <= midY) || ordered.Skip(11).Any(c => c.Home.y > midY))
				throw new InvalidOperationException("The conveyor must have two runs of eleven cells.");
			_nodes = ordered.Select(c => c.Home).ToArray();
			for (int i = 0; i < ordered.Length; i++) ordered[i].Rank = i;
			_loadingRank = Enumerable.Range(0, 11).OrderBy(i => Mathf.Abs(_nodes[i].x)).First();
			_loading = _nodes[_loadingRank];
			_direction = Quaternion.Euler(-ReloadDip.LoadAngle, 0, 0) * Vector3.forward;
			var breech = vehicle.GetComponent<ModuleBuilder.ModuleSet>().Built.First(m => m.m.Id == "xray_breech");
			var template = breech.dc.GetComponent<AarVisual>();
			_sourceBreech = template;
			_metal = new Material(template.AarMaterial) { name = "Leclerc conveyor steel (inferred)", color = new Color(0.37f, 0.43f, 0.47f, 0.85f) };
			_light = new Material(template.AarMaterial) { name = "Leclerc loading mechanism (inferred)", color = new Color(0.66f, 0.72f, 0.69f, 0.95f) };
			if (_cellMesh == null) _cellMesh = CarrierMesh();
			if (_box == null) _box = BoxMesh();
			foreach (var cell in _cells)
			{
				cell.Carrier = Part("cell " + cell.SlotIndex, _root, _cellMesh, _metal);
				cell.Carrier.localPosition = cell.Home;
			}
			var gunBone = RigBones.Gun(vehicle.transform);
			var axis = vehicle.transform.InverseTransformPoint(mainMuzzle(vehicle));
			var rear = _turret.InverseTransformPoint(vehicle.transform.TransformPoint(new Vector3(axis.x, axis.y, breech.m.Bounds.min.z)));
			var casing = ApertureMesh(breech.m.XrayVerts, breech.m.XrayTris, vehicle.transform, gunBone, axis.x, axis.y);
			_ownedMeshes.Add(casing);
			Part("CN120 breech casing (visual aperture)", gunBone, casing, template.AarMaterial);
			template.Hidden = true;
			_stroke = Mathf.Max(0.5f, rear.z + 0.12f - _loading.z);
			float doorZ = _loading.z + 1.04f;
			_doorRest = new Vector3(_loading.x, _loading.y, doorZ);
			_door = Box("loading door", _root, _doorRest, new Vector3(0.25f, 0.27f, 0.035f), _light);
			float gutterLength = Mathf.Max(0.12f, rear.z - doorZ);
			var gutter = _gutter = new GameObject("fiberglass gutter (inferred)").transform;
			gutter.SetParent(_root, false);
			gutter.localPosition = new Vector3(_loading.x, _loading.y, doorZ);
			gutter.localRotation = Quaternion.LookRotation(_direction);
			Box("gutter floor", gutter, new Vector3(0, -0.075f, gutterLength * 0.5f), new Vector3(0.17f, 0.018f, gutterLength), _light);
			foreach (float x in new[] { -0.09f, 0.09f })
				Box("gutter side", gutter, new Vector3(x, -0.035f, gutterLength * 0.5f), new Vector3(0.018f, 0.08f, gutterLength), _light);
			for (int i = 0; i < 3; i++)
				_rammer.Add(Box("telescopic rammer " + i, _root, _loading - Vector3.forward * 0.05f,
					new Vector3(0.055f - i * 0.012f, 0.055f - i * 0.012f, 0.04f), _light));
			var rearWorld = vehicle.transform.TransformPoint(new Vector3(axis.x, axis.y, breech.m.Bounds.min.z + 0.195f));
			_blockRest = gunBone.InverseTransformPoint(rearWorld);
			_block = Box("sliding breechblock (inferred)", gunBone, _blockRest, new Vector3(0.25f, 0.26f, 0.33f), _light);
			_block.localRotation = Quaternion.Inverse(gunBone.rotation) * vehicle.transform.rotation;
			_blockTravel = gunBone.InverseTransformVector(vehicle.transform.TransformVector(Vector3.down * 0.30f));
			Pose(0);
		}

		static Vector3 mainMuzzle(Vehicle vehicle) => vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon.MuzzleIdentity.position;

		internal Vector3 Sample(float phase)
		{
			float p = (float)LoaderMotion.Wrap(phase);
			int i = Mathf.FloorToInt(p), next = (i + 1) % _nodes.Length;
			float t = p - i;
			var a = _nodes[i]; var b = _nodes[next];
			if (i != 10 && i != 21) return Vector3.Lerp(a, b, t);
			float r = Mathf.Abs(a.y - b.y) * 0.5f;
			return new Vector3(Mathf.Lerp(a.x, b.x, t) + (i == 10 ? -1 : 1) * r * Mathf.Sin(t * Mathf.PI),
				(a.y + b.y) * 0.5f + (a.y - b.y) * 0.5f * Mathf.Cos(t * Mathf.PI), Mathf.Lerp(a.z, b.z, t));
		}

		bool PickCell(AmmoType.AmmoClip clip)
		{
			if (clip?.MinimalPattern == null || clip.MinimalPattern.Length == 0) return false;
			var type = clip.MinimalPattern[0].AmmoType;
			var choice = _cells.Where(c => _feed.ReadyRack.GetAmmoTypeInSlot(c.SlotIndex) == type)
				.OrderBy(c => LoaderMotion.Advance(c.Rank, _phase, _loadingRank)).FirstOrDefault();
			if (choice == null) return false;
			_target = choice.SlotIndex;
			_feed.Carousel.RotateToSlot(_target);
			return true;
		}

		void Stage(int stage)
		{
			if (stage != 0 || _running) return;
			var cell = _cells.FirstOrDefault(c => c.SlotIndex == _target);
			if (cell == null) return;
			_startPhase = _phase;
			_goalPhase = _phase + (float)LoaderMotion.Advance(cell.Rank, _phase, _loadingRank);
			_round = cell.Slot.GetComponentsInChildren<AmmoStoredVisual>().FirstOrDefault(v => !v.Hidden)?.transform;
			if (_round != null) _roundRest = _round.localPosition;
			_running = true;
		}

		void LateUpdate()
		{
			if (!_running || _feed == null) return;
			if (_feed.Reloading)
				Pose(Mathf.Clamp01((float)Clock.GetValue(_feed) / Mathf.Max(0.01f, _feed.TotalReloadTime)));
			else
			{
				if (_feed.AmmoTypeInBreech != null) Pose(1);
				if (_round != null) _round.localPosition = _roundRest;
				_round = null; _running = false;
				Rest();
			}
		}

		void Pose(float progress)
		{
			_phase = Mathf.Lerp(_startPhase, _goalPhase, (float)LoaderMotion.Index(progress));
			foreach (var cell in _cells)
			{
				var p = Sample(cell.Rank + _phase);
				cell.Slot.position = _turret.TransformPoint(p);
				cell.Carrier.localPosition = p;
			}
			_door.localPosition = _doorRest + Vector3.right * (0.29f * (float)LoaderMotion.Door(progress));
			_block.localPosition = _blockRest + _blockTravel * (float)LoaderMotion.Breech(progress);
			float deployment = (float)LoaderMotion.Gutter(progress);
			_gutter.localScale = new Vector3(1, 1, deployment);
			_gutter.gameObject.SetActive(deployment > 0);
			float extension = _stroke * (float)LoaderMotion.Ram(progress);
			for (int i = 0; i < _rammer.Count; i++)
			{
				float length = Mathf.Max(0.035f, extension / 3);
				var r = _rammer[i];
				r.localRotation = Quaternion.LookRotation(_direction);
				r.localPosition = _loading - _direction * 0.07f + _direction * (extension * i / 3 + length * 0.5f);
				r.localScale = new Vector3(0.055f - i * 0.012f, 0.055f - i * 0.012f, length);
			}
			if (_round != null)
			{
				var slot = _round.parent;
				_round.position = slot.TransformPoint(_roundRest) + _turret.TransformDirection(_direction) * (_stroke * (float)LoaderMotion.Round(progress));
			}
		}

		void Rest()
		{
			if (_door != null) _door.localPosition = _doorRest;
			if (_block != null) _block.localPosition = _blockRest;
			if (_gutter != null) _gutter.gameObject.SetActive(false);
			foreach (var r in _rammer) { var s = r.localScale; s.z = 0.035f; r.localScale = s; r.localPosition = _loading - _direction * 0.07f; }
		}

		void OnDisable()
		{
			if (_round != null) _round.localPosition = _roundRest;
			_running = false;
			Rest();
		}

		void OnDestroy()
		{
			if (_feed != null) _feed.ReloadStageStarted -= Stage;
			if (_metal != null) Destroy(_metal);
			if (_light != null) Destroy(_light);
			foreach (var mesh in _ownedMeshes) if (mesh != null) Destroy(mesh);
		}

		[HarmonyPatch(typeof(AmmoCarousel), nameof(AmmoCarousel.SeekSlotWithClipType))]
		static class Seek
		{
			static bool Prefix(AmmoCarousel __instance, AmmoType.AmmoClip clipType)
			{
				var loader = __instance.Feed?.GetComponent<ConveyorLoader>();
				if (loader == null || !loader.isActiveAndEnabled) return true;
				try { return !loader.PickCell(clipType); }
				catch (Exception e) { PatchGuard.Failed("Leclerc conveyor selection", e); return true; }
			}
		}

		static readonly FieldInfo ReloadEvent = Reflect.Field(typeof(AmmoFeed), "_clipReloadFMODEventInstance");
		static readonly FieldInfo FeedStage = Reflect.Field(typeof(AmmoFeed), "_clipFeedStage");
		[HarmonyPatch(typeof(AmmoFeed), "FinishClipReload")]
		static class LetTailPlay
		{
			static void Prefix(AmmoFeed __instance)
			{
				try
				{
					if (ReloadEvent == null || FeedStage == null || __instance.GetComponent<ConveyorLoader>() == null) return;
					if ((int)FeedStage.GetValue(__instance) != __instance.ClipReloadStages.Length - 1) return;
					var e = (EventInstance)ReloadEvent.GetValue(__instance);
					if (!e.isValid()) return;
					e.release();
					ReloadEvent.SetValue(__instance, default(EventInstance));
				}
				catch (Exception x) { PatchGuard.Failed("Leclerc reload sound tail", x); }
			}
		}

		Transform Part(string name, Transform parent, Mesh mesh, Material material)
		{
			var go = new GameObject(name);
			go.SetActive(false);
			go.transform.SetParent(parent, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			var av = go.AddComponent<AarVisual>();
			av.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
			av.AarMaterial = material;
			go.SetActive(true);
			_parts.Add(go.transform);
			return go.transform;
		}

		Transform Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
		{
			var t = Part(name, parent, _box, material);
			t.localPosition = position; t.localScale = size;
			return t;
		}

		static Mesh ApertureMesh(Vector3[] vertices, int[] triangles, Transform prefab, Transform gun, float x, float y)
		{
			var output = new List<Vector3>(); var indices = new List<int>();
			var planes = new[] { new Vector4(1,0,0,-x+.075f), new Vector4(-1,0,0,x+.075f), new Vector4(0,1,0,-y+.075f), new Vector4(0,-1,0,y+.075f) };
			void Emit(List<Vector3> polygon) {
				int start = output.Count;
				output.AddRange(polygon.Select(v => gun.InverseTransformPoint(prefab.TransformPoint(v))));
				for (int j = 1; j < polygon.Count - 1; j++) indices.AddRange(new[] { start, start + j, start + j + 1 });
			}
			List<Vector3> Clip(List<Vector3> polygon, Vector4 plane, bool inside) {
				var result = new List<Vector3>();
				if (polygon.Count == 0) return result;
				float Distance(Vector3 p) => p.x * plane.x + p.y * plane.y + plane.w;
				var a = polygon[polygon.Count - 1]; float da = Distance(a);
				foreach (var b in polygon) {
					float db = Distance(b); bool keepA = inside ? da >= 0 : da <= 0, keepB = inside ? db >= 0 : db <= 0;
					if (keepA != keepB) result.Add(Vector3.Lerp(a, b, da / (da - db)));
					if (keepB) result.Add(b);
					a = b; da = db;
				}
				return result;
			}
			for (int i = 0; i < triangles.Length; i += 3) {
				var polygon = new List<Vector3> { vertices[triangles[i]], vertices[triangles[i+1]], vertices[triangles[i+2]] };
				foreach (var plane in planes) { Emit(Clip(polygon, plane, false)); polygon = Clip(polygon, plane, true); }
			}
			int count = output.Count, tri = indices.Count;
			output.AddRange(output.GetRange(0, count));
			for (int j = 0; j < tri; j += 3) indices.AddRange(new[] { indices[j] + count, indices[j + 2] + count, indices[j + 1] + count });
			var mesh = new Mesh { name = "CN120 visual casing with loading aperture (inferred)", vertices = output.ToArray(), triangles = indices.ToArray() };
			mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
		}

		static Mesh BoxMesh()
		{
			var v = new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
				new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
			var m = new Mesh { name = "Leclerc loader box", vertices = v, triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5} };
			m.RecalculateNormals(); m.RecalculateBounds(); return m;
		}

		static Mesh CarrierMesh()
		{
			var verts = new List<Vector3>(); var tris = new List<int>();
			for (int i = 0; i <= 12; i++)
			{
				float a = (180f + i * 15f) * Mathf.Deg2Rad;
				foreach (float r in new[] { 0.077f, 0.084f })
					foreach (float z in new[] { 0.015f, 0.985f }) verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
				if (i == 12) continue;
				int n = i * 4;
				foreach (var q in new[] { new[] {n,n+4,n+5,n+1}, new[] {n+2,n+3,n+7,n+6}, new[] {n,n+2,n+6,n+4}, new[] {n+1,n+5,n+7,n+3} })
					tris.AddRange(new[] {q[0],q[1],q[2],q[0],q[2],q[3]});
			}
			var m = new Mesh { name = "Leclerc conveyor U cell (inferred)", vertices = verts.ToArray(), triangles = tris.ToArray() };
			m.RecalculateNormals(); m.RecalculateBounds(); return m;
		}
	}
}
