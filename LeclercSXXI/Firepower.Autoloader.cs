using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;
using HarmonyLib;
using UnityEngine;

namespace LeclercSXXI
{
	[DefaultExecutionOrder(10000)]
	internal sealed partial class ConveyorLoader : MonoBehaviour
	{
		static readonly System.Reflection.FieldInfo Clock = Reflect.Field(typeof(AmmoFeed), "_clipFeedTime");
		static readonly System.Reflection.FieldInfo Duration = Reflect.Field(typeof(AmmoFeed.ReloadStage), "Duration");
		const int RunCells = LoaderMotion.Capacity / 2;   // each run of the chain, upper and lower
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

		// Called before GunSetup scales durations. Fresh stages avoid mutating the donor's stage objects.
		internal static void Configure(Vehicle vehicle)
		{
			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0]?.Weapon?.Feed;
			if (feed == null || Duration == null) return;
			// The stage seconds are my estimate: they add up to the maker's 6 s for the same round, and GunSetup stretches them to
			// the reload time (8 s when the round changes).
			float[] seconds = { 2f, 0.64f, 1.2f, 0.96f, 0.6f, 0.48f, 0.12f };
			feed.ClipReloadStages = seconds.Select((duration, index) =>
			{
				// The game's autoloader event loops the carousel spin while WaitForCarousel is 1 (its initial value) and moves
				// on when it drops to 0: 1 only while the conveyor indexes (stage 0), 0 from the loading door onward, so the
				// sound follows the animation.
				var stage = new AmmoFeed.ReloadStage {
					AnimatedParts = new AnimatedPart[0],
					StageClips = new AudioClip[0],
					ClipReloadFMODParameters = new[] { new AmmoFeed.ReloadStage.FMODParameter
					{
						Name = "WaitForCarousel",
						Value = index == 0 ? 1f : 0f
					} },
					LiveDuration = duration,
				};
				Duration.SetValue(stage, duration);
				return stage;
			}).ToArray();
		}

		// After PortAxes, before the racks fill. Slot identities and damage/flammables remain owned by GHPC.
		internal static void Attach(Vehicle vehicle)
		{
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons[0];
			var feed = main?.Weapon?.Feed;
			if (feed == null || feed.GetComponent<ConveyorLoader>() != null) return;
			var rack = feed.ReadyRack;
			var turret = main.FCS?.TurretPlatform?.Transform;
			if (turret == null || rack?.VisualSlots?.Count != LoaderMotion.Capacity || feed.Carousel == null || Clock == null)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: conveyor animation could not find its 22 cells and gun feed.");
				return;
			}
			var loader = feed.gameObject.AddComponent<ConveyorLoader>();
			if (!loader.Fit(vehicle, feed, turret)) return;
			feed.ReloadStageStarted += loader.Stage;
			// The hand load from the hull racks gets its sound component here.
			feed.gameObject.AddComponent<StorageLoadSound>().Watch(feed);
		}

		// The animation built on this feed. When that fails the breech's own drawing comes back and none of it shows.
		bool Fit(Vehicle vehicle, AmmoFeed feed, Transform turret)
		{
			_feed = feed;
			_turret = turret;
			try
			{
				Build(vehicle);
				return true;
			}
			catch (Exception exception)
			{
				enabled = false;
				if (_sourceBreech != null) _sourceBreech.Hidden = false;
				foreach (var part in _parts) if (part != null) part.gameObject.SetActive(false);
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: conveyor animation could not be fitted: " + exception.Message);
				return false;
			}
		}

		void Build(Vehicle vehicle)
		{
			_root = new GameObject("LeclercSXXI conveyor animation").transform;
			_root.SetParent(_turret, false);
			for (int index = 0; index < _feed.ReadyRack.VisualSlots.Count; index++)
			{
				var slot = _feed.ReadyRack.VisualSlots[index];
				_cells.Add(new Cell { SlotIndex = index, Slot = slot, Home = _turret.InverseTransformPoint(slot.position) });
			}
			float midY = _cells.Average(cell => cell.Home.y);
			var ordered = _cells.Where(cell => cell.Home.y > midY).OrderByDescending(cell => cell.Home.x)
				.Concat(_cells.Where(cell => cell.Home.y <= midY).OrderBy(cell => cell.Home.x)).ToArray();
			if (ordered.Take(RunCells).Any(cell => cell.Home.y <= midY) || ordered.Skip(RunCells).Any(cell => cell.Home.y > midY))
				throw new InvalidOperationException("The conveyor must have two runs of eleven cells.");
			_nodes = ordered.Select(cell => cell.Home).ToArray();
			for (int index = 0; index < ordered.Length; index++) ordered[index].Rank = index;
			_loadingRank = Enumerable.Range(0, RunCells).OrderBy(i => Mathf.Abs(_nodes[i].x)).First();
			_loading = _nodes[_loadingRank];
			_direction = Quaternion.Euler(-ReloadDip.LoadAngle, 0, 0) * Vector3.forward;
			// Imported modules live in follower groups at scene root. Resolve by their module identity, not display name.
			var breech = vehicle.GetComponent<ModuleBuilder.ModuleSet>().Built.First(module => module.m.Id == "xray_breech");
			var template = breech.dc.GetComponent<AarVisual>();
			_sourceBreech = template;
			// The two colours are my choice: no picture gives the loader's paint.
			_metal = new Material(template.AarMaterial)
			{
				name = "Leclerc conveyor steel (inferred)",
				color = new Color(0.37f, 0.43f, 0.47f, 0.85f)
			};
			_light = new Material(template.AarMaterial)
			{
				name = "Leclerc loading mechanism (inferred)",
				color = new Color(0.66f, 0.72f, 0.69f, 0.95f)
			};
			if (_cellMesh == null) _cellMesh = CarrierMesh();
			if (_box == null) _box = BoxMesh();
			foreach (var cell in _cells)
			{
				// A sibling, not a child of the ammo placeholder: GHPC deletes every placeholder child on retrieval.
				cell.Carrier = Part("cell " + cell.SlotIndex, _root, _cellMesh, _metal);
				cell.Carrier.localPosition = cell.Home;
			}
			var gunBone = RigBones.Gun(vehicle.transform);
			// Use the chamber's hit-zone rear plane: the drawing also includes a lever extending aft below the chamber.
			var axis = vehicle.transform.InverseTransformPoint(MainMuzzle(vehicle));
			var rear = _turret.InverseTransformPoint(vehicle.transform.TransformPoint(new Vector3(axis.x, axis.y, breech.m.Bounds.min.z)));
			var casing = ApertureMesh(breech.m.XrayVerts, breech.m.XrayTris, vehicle.transform, gunBone, axis.x, axis.y);
			_ownedMeshes.Add(casing);
			Part("CN120 breech casing (visual aperture)", gunBone, casing, template.AarMaterial);
			template.Hidden = true;   // drawing replaced, but its report zone and every working gun hit zone stay
			// The stored model's origin is the case base. End the stroke just inside the breech's rear plane.
			_stroke = Mathf.Max(0.5f, rear.z + 0.12f - _loading.z);
			// The sizes and travels of the door, the gutter, the rammer and the breechblock below are my estimates, drawn to
			// fit the cell and the breech, because no drawing of the real loader gives them.
			float doorZ = _loading.z + 1.04f;
			_doorRest = new Vector3(_loading.x, _loading.y, doorZ);
			_door = Box("loading door", _root, _doorRest, new Vector3(0.25f, 0.27f, 0.035f), _light);
			// Gutter: open above, along the loading axis from the cell's mouth to the breech.
			float gutterLength = Mathf.Max(0.12f, rear.z - doorZ);
			var gutter = _gutter = new GameObject("fiberglass gutter (inferred)").transform;
			gutter.SetParent(_root, false);
			gutter.localPosition = new Vector3(_loading.x, _loading.y, doorZ);
			gutter.localRotation = Quaternion.LookRotation(_direction);
			Box("gutter floor", gutter, new Vector3(0, -0.075f, gutterLength * 0.5f), new Vector3(0.17f, 0.018f, gutterLength), _light);
			foreach (float side in new[] { -0.09f, 0.09f })
				Box("gutter side", gutter, new Vector3(side, -0.035f, gutterLength * 0.5f),
					new Vector3(0.018f, 0.08f, gutterLength), _light);
			for (int stage = 0; stage < 3; stage++)
				_rammer.Add(Box("telescopic rammer " + stage, _root, _loading - Vector3.forward * 0.05f,
					new Vector3(0.055f - stage * 0.012f, 0.055f - stage * 0.012f, 0.04f), _light));
			// Front face 36 cm inside the casing's rear plane, rear face 3 cm inside it: the closed block covers the
			// rammed case base (stopped 12 cm inside) by 9 cm and still never protrudes.
			var rearWorld = vehicle.transform.TransformPoint(new Vector3(axis.x, axis.y, breech.m.Bounds.min.z + 0.195f));
			_blockRest = gunBone.InverseTransformPoint(rearWorld);
			_block = Box("sliding breechblock (inferred)", gunBone, _blockRest, new Vector3(0.25f, 0.26f, 0.33f), _light);
			// The donor bone's local axes need not match the vehicle: resolve vertical travel explicitly.
			_block.localRotation = Quaternion.Inverse(gunBone.rotation) * vehicle.transform.rotation;
			_blockTravel = gunBone.InverseTransformVector(vehicle.transform.TransformVector(Vector3.down * 0.30f));
			Pose(0);
		}

		static Vector3 MainMuzzle(Vehicle vehicle) => vehicle.GetComponent<WeaponsManager>().Weapons[0].Weapon.MuzzleIdentity.position;

		internal Vector3 Sample(float phase)
		{
			float wrapped = (float)LoaderMotion.Wrap(phase);
			int node = Mathf.FloorToInt(wrapped), next = (node + 1) % _nodes.Length;
			float fraction = wrapped - node;
			var start = _nodes[node];
			var end = _nodes[next];
			if (node != RunCells - 1 && node != LoaderMotion.Capacity - 1) return Vector3.Lerp(start, end, fraction);
			// Semicircular end turns. Shells stay nose-forward while their carriers follow the chain.
			float radius = Mathf.Abs(start.y - end.y) * 0.5f;
			return new Vector3(
				Mathf.Lerp(start.x, end.x, fraction) + (node == RunCells - 1 ? -1 : 1) * radius * Mathf.Sin(fraction * Mathf.PI),
				(start.y + end.y) * 0.5f + (start.y - end.y) * 0.5f * Mathf.Cos(fraction * Mathf.PI),
				Mathf.Lerp(start.z, end.z, fraction));
		}

		bool PickCell(AmmoType.AmmoClip clip)
		{
			if (clip?.MinimalPattern == null || clip.MinimalPattern.Length == 0) return false;
			var type = clip.MinimalPattern[0].AmmoType;
			var choice = _cells.Where(cell => _feed.ReadyRack.GetAmmoTypeInSlot(cell.SlotIndex) == type)
				.OrderBy(cell => LoaderMotion.Advance(cell.Rank, _phase, _loadingRank)).FirstOrDefault();
			if (choice == null) return false;
			_target = choice.SlotIndex;
			_feed.Carousel.RotateToSlot(_target);
			return true;
		}

		void Stage(int stage)
		{
			if (stage != 0) return;
			// Used once. A load with no seek before it (another mod's, from the hull racks) must not replay the last cell.
			int target = _target;
			_target = -1;
			if (_running) return;
			var cell = _cells.FirstOrDefault(candidate => candidate.SlotIndex == target);
			if (cell == null) return;
			_startPhase = _phase;
			_goalPhase = _phase + (float)LoaderMotion.Advance(cell.Rank, _phase, _loadingRank);
			_round = cell.Slot.GetComponentsInChildren<AmmoStoredVisual>().FirstOrDefault(visual => !visual.Hidden)?.transform;
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
				// Complete only a successful load. A cancelled reload leaves the conveyor where it actually is.
				if (_feed.AmmoTypeInBreech != null) Pose(1);
				if (_round != null) _round.localPosition = _roundRest;
				_round = null;
				_running = false;
				Rest();
			}
		}

		void Pose(float progress)
		{
			_phase = Mathf.Lerp(_startPhase, _goalPhase, (float)LoaderMotion.Index(progress));
			foreach (var cell in _cells)
			{
				var position = Sample(cell.Rank + _phase);
				cell.Slot.position = _turret.TransformPoint(position);
				cell.Carrier.localPosition = position;
			}
			_door.localPosition = _doorRest + Vector3.right * (0.29f * (float)LoaderMotion.Door(progress));
			_block.localPosition = _blockRest + _blockTravel * (float)LoaderMotion.Breech(progress);
			float deployment = (float)LoaderMotion.Gutter(progress);
			_gutter.localScale = new Vector3(1, 1, deployment);
			_gutter.gameObject.SetActive(deployment > 0);
			float extension = _stroke * (float)LoaderMotion.Ram(progress);
			for (int index = 0; index < _rammer.Count; index++)
			{
				float length = Mathf.Max(0.035f, extension / 3);
				var rod = _rammer[index];
				rod.localRotation = Quaternion.LookRotation(_direction);
				rod.localPosition = _loading - _direction * 0.07f + _direction * (extension * index / 3 + length * 0.5f);
				rod.localScale = new Vector3(0.055f - index * 0.012f, 0.055f - index * 0.012f, length);
			}
			if (_round != null)
			{
				var slot = _round.parent;
				_round.position = slot.TransformPoint(_roundRest)
					+ _turret.TransformDirection(_direction) * (_stroke * (float)LoaderMotion.Round(progress));
			}
		}

		void Rest()
		{
			if (_door != null) _door.localPosition = _doorRest;
			if (_block != null) _block.localPosition = _blockRest;
			if (_gutter != null) _gutter.gameObject.SetActive(false);
			foreach (var rod in _rammer)
			{
				var scale = rod.localScale;
				scale.z = 0.035f;
				rod.localScale = scale;
				rod.localPosition = _loading - _direction * 0.07f;
			}
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

		// Only this port's carousel is intercepted. Vanilla carousels and other ports keep their own search.
		[HarmonyPatch(typeof(AmmoCarousel), nameof(AmmoCarousel.SeekSlotWithClipType))]
		static class Seek
		{
			static bool Prefix(AmmoCarousel __instance, AmmoType.AmmoClip clipType)
			{
				var loader = __instance.Feed?.GetComponent<ConveyorLoader>();
				if (loader == null || !loader.isActiveAndEnabled) return true;
				try { return !loader.PickCell(clipType); }
				catch (Exception exception)
				{
					PatchGuard.Failed("Leclerc conveyor selection", exception);
					return true;
				}
			}
		}

		// A finished reload stops the autoloader event at once. Its tail (ram, breech closing) starts only when the carousel
		// loop exits after WaitForCarousel drops, which lands anywhere in the loop, so on a 6 s cycle the end was cut some of
		// the time. On a completed load, hand the event over to play out: released, not stopped, it frees itself at its end.
		// Cancels and destruction still stop it through the game's own path.
		static readonly System.Reflection.FieldInfo ReloadEvent = Reflect.Field(typeof(AmmoFeed), "_clipReloadFMODEventInstance");
		static readonly System.Reflection.FieldInfo FeedStage = Reflect.Field(typeof(AmmoFeed), "_clipFeedStage");
		[HarmonyPatch(typeof(AmmoFeed), "FinishClipReload")]
		static class LetTailPlay
		{
			static void Prefix(AmmoFeed __instance)
			{
				try
				{
					if (ReloadEvent == null || FeedStage == null || __instance.GetComponent<ConveyorLoader>() == null) return;
					if ((int)FeedStage.GetValue(__instance) != __instance.ClipReloadStages.Length - 1) return;
					var tail = (FMOD.Studio.EventInstance)ReloadEvent.GetValue(__instance);
					if (!tail.isValid()) return;
					tail.release();
					ReloadEvent.SetValue(__instance, default(FMOD.Studio.EventInstance));
				}
				catch (Exception exception) { PatchGuard.Failed("Leclerc reload sound tail", exception); }
			}
		}

		Transform Part(string name, Transform parent, Mesh mesh, Material material)
		{
			var part = new GameObject(name);
			part.SetActive(false);
			part.transform.SetParent(parent, false);
			part.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = part.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			var visual = part.AddComponent<AarVisual>();
			visual.RenderMode = AarVisual.AarVisualRenderMode.XrayOnly;
			visual.AarMaterial = material;
			part.SetActive(true);
			_parts.Add(part.transform);
			return part.transform;
		}
		Transform Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
		{
			var part = Part(name, parent, _box, material);
			part.localPosition = position;
			part.localScale = size;
			return part;
		}
		// Subtract a 15 cm square visual bore from a private copy of the imported casing. Damage geometry is untouched.
		// Each triangle is cut by four half-planes and only the pieces outside the aperture are kept.
		static Mesh ApertureMesh(Vector3[] vertices, int[] triangles, Transform prefab, Transform gun, float axisX, float axisY)
		{
			var output = new List<Vector3>();
			var indices = new List<int>();
			var planes = new[]
			{
				new Vector4(1, 0, 0, -axisX + 0.075f),
				new Vector4(-1, 0, 0, axisX + 0.075f),
				new Vector4(0, 1, 0, -axisY + 0.075f),
				new Vector4(0, -1, 0, axisY + 0.075f)
			};
			void Emit(List<Vector3> polygon)
			{
				int start = output.Count;
				output.AddRange(polygon.Select(vertex => gun.InverseTransformPoint(prefab.TransformPoint(vertex))));
				for (int corner = 1; corner < polygon.Count - 1; corner++)
					indices.AddRange(new[] { start, start + corner, start + corner + 1 });
			}
			List<Vector3> Clip(List<Vector3> polygon, Vector4 plane, bool inside)
			{
				var result = new List<Vector3>();
				if (polygon.Count == 0) return result;
				float Distance(Vector3 point) => point.x * plane.x + point.y * plane.y + plane.w;
				var previous = polygon[polygon.Count - 1];
				float previousDistance = Distance(previous);
				foreach (var current in polygon)
				{
					float currentDistance = Distance(current);
					bool keepPrevious = inside ? previousDistance >= 0 : previousDistance <= 0;
					bool keepCurrent = inside ? currentDistance >= 0 : currentDistance <= 0;
					if (keepPrevious != keepCurrent)
						result.Add(Vector3.Lerp(previous, current, previousDistance / (previousDistance - currentDistance)));
					if (keepCurrent) result.Add(current);
					previous = current;
					previousDistance = currentDistance;
				}
				return result;
			}
			for (int index = 0; index < triangles.Length; index += 3)
			{
				var polygon = new List<Vector3>
				{
					vertices[triangles[index]],
					vertices[triangles[index + 1]],
					vertices[triangles[index + 2]]
				};
				foreach (var plane in planes)
				{
					Emit(Clip(polygon, plane, false));
					polygon = Clip(polygon, plane, true);
				}
			}
			// The drawing is a single-sided shell: culled back faces left see-through gaps around the cut and thin spots. Add a
			// reversed copy on its own vertices so the inside is lit as a wall too (normals are recalculated per side).
			int count = output.Count, tri = indices.Count;
			output.AddRange(output.GetRange(0, count));
			for (int index = 0; index < tri; index += 3)
				indices.AddRange(new[] { indices[index] + count, indices[index + 2] + count, indices[index + 1] + count });
			var mesh = new Mesh
			{
				name = "CN120 visual casing with loading aperture (inferred)",
				vertices = output.ToArray(),
				triangles = indices.ToArray()
			};
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
		static Mesh BoxMesh()
		{
			var corners = new[]
			{
				new Vector3(-.5f,-.5f,-.5f),
				new Vector3(.5f,-.5f,-.5f),
				new Vector3(.5f,.5f,-.5f),
				new Vector3(-.5f,.5f,-.5f),
				new Vector3(-.5f,-.5f,.5f),
				new Vector3(.5f,-.5f,.5f),
				new Vector3(.5f,.5f,.5f),
				new Vector3(-.5f,.5f,.5f) };
			var mesh = new Mesh
			{
				name = "Leclerc loader box",
				vertices = corners,
				triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5}
			};
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
		static Mesh CarrierMesh()
		{
			var verts = new List<Vector3>();
			var tris = new List<int>();
			// Open U-section cradle, radius 8 cm, thin walls, 97 cm fore-aft. Shaped roughly after pictures of the real loader.
			for (int step = 0; step <= 12; step++)
			{
				float angle = (180f + step * 15f) * Mathf.Deg2Rad;
				foreach (float radius in new[] { 0.077f, 0.084f })
					foreach (float depth in new[] { 0.015f, 0.985f })
						verts.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, depth));
				if (step == 12) continue;
				int first = step * 4;
				var quads = new[]
				{
					new[] { first, first + 4, first + 5, first + 1 },
					new[] { first + 2, first + 3, first + 7, first + 6 },
					new[] { first, first + 2, first + 6, first + 4 },
					new[] { first + 1, first + 5, first + 7, first + 3 }
				};
				foreach (var quad in quads)
					tris.AddRange(new[] { quad[0], quad[1], quad[2], quad[0], quad[2], quad[3] });
			}
			var mesh = new Mesh { name = "Leclerc conveyor U cell (inferred)", vertices = verts.ToArray(), triangles = tris.ToArray() };
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
