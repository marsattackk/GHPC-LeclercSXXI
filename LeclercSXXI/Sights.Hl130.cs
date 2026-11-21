using System.Collections.Generic;
using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using LeclercSXXI.Common;
using Reticle;
using UnityEngine;

namespace LeclercSXXI
{
	// The HL 130 day reticle (Resources\hl130_day.jsonc, drawn from pictures of the real sight): the nominal cross on the line
	// of sight, the plate (drop scales, stadia, mil scale, emergency mark) moved by the gun's lay against it. The pictures put
	// the plate 7.6 to 23.6 mil above the cross and once 10 mil to its side while the cross and display box keep their place.
	// Inferred: the plate shows the gun's direction (the fire control's lay of superelevation and lead, not the barrel itself,
	// so the loading dip doesn't drag it). Not the parallax: the gun still converges on the line of sight, but the plate
	// jumped 5.6 mil sideways at 100 m, 1.9 at 300 m (the head 0.56 m beside the bore), at every range step, as seen in game.
	// Each drop scale's index (the French diagram's "<" / ">") points at the set range on its own round's scale and moves
	// with the plate: INFERRED, no source says what drives it. GHPC moves a reticle part only by its group's alignment, one
	// bone per alignment, so each index has an alignment of its own that this class drives in place of the game (none of
	// the three means here what its name says). The game also turns the RotaryRange bone by the range: put back straight.
	[DefaultExecutionOrder(20)]
	internal sealed class GunnerDayReticle : MonoBehaviour
	{
		const string File = "hl130_day.jsonc";
		static readonly System.Reflection.FieldInfo ParallaxOn = Reflect.Field(typeof(FireControlSystem), "_fixParallaxForVectorMode");
		static readonly System.Reflection.FieldInfo LayRound = Reflect.Field(typeof(FireControlSystem), "_bcAmmo");
		static readonly (ReticleTree.GroupBase.Alignment Align, string Round, string Top)[] RangeIndices =
		{
			(ReticleTree.GroupBase.Alignment.LasePoint, "m2", "Top127"),
			(ReticleTree.GroupBase.Alignment.RotaryRange, "oecc", "TopOecc"),
			(ReticleTree.GroupBase.Alignment.VerticalRange, "ofl", "TopOfl"),
		};
		UsableOptic _optic;
		AmmoType[] _indexRounds;
		float[] _indexTops;

		internal static ReticleSO Fit(Vehicle vehicle, UsableOptic optic, ReticleSO fallback)
		{
			var loadout = vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			AmmoCodexScriptable FirstRound(AmmoClipCodexScriptable clip) => clip?.ClipType?.MinimalPattern?.FirstOrDefault();
			var apfsdsClip = loadout?.FirstOrDefault(clip => clip != null && PortAmmo.Apfsds.Values.Contains(clip));
			var rounds = new Dictionary<string, AmmoCodexScriptable>
			{
				["ofl"] = FirstRound(apfsdsClip),
				["oecc"] = FirstRound(PortAmmo.Heat),
				["m2"] = Coax.Round,
			};
			var host = optic.reticleMesh?.reticleSO;
			var fontHost = ReticleBuild.FontHost(host, vehicle.transform);
			var font = new[] { ReticleBuild.Font("Reticle LiberationSans SDF"), ReticleBuild.Font("LiberationSans SDF"),
				ReticleBuild.FontTemplate(fontHost)?.font }
				.FirstOrDefault(candidate => candidate != null && ReticleBuild.Missing(candidate, "OFL OECC 12.7 0123456789").Length == 0);
			var missing = rounds.Where(entry => entry.Value == null).Select(entry => entry.Key).ToArray();
			if (host == null || fontHost == null || font == null || missing.Length > 0)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: using the donor day reticle (host '{host?.name}', "
					+ $"label template '{fontHost?.name}', usable font '{font?.name}', "
					+ $"missing scales [{string.Join(", ", missing)}]).");
				return fallback;
			}

			var marks = Reticles.Hl130Day.Build(File, host, fontHost, rounds);
			foreach (var label in ReticleBuild.AllOf<ReticleTree.Text>(marks)) label.font = font;
			var spec = Reticles.Hl130Day.Spec(File);
			var indexRounds = RangeIndices.Select(index => rounds[index.Round].AmmoType).ToArray();
			var indexTops = RangeIndices.Select(index => (float)spec.Num(index.Top)).ToArray();
			optic.gameObject.AddComponent<GunnerDayReticle>().Init(optic, indexRounds, indexTops);
			return marks;
		}

		void Init(UsableOptic optic, AmmoType[] indexRounds, float[] indexTops)
		{
			_optic = optic;
			_indexRounds = indexRounds;
			_indexTops = indexTops;
		}

		void LateUpdate()
		{
			if (_optic?.slot == null || !_optic.slot.IsActive || _optic.FCS == null || _optic.reticleMesh == null) return;
			var fcs = _optic.FCS;
			var mesh = _optic.reticleMesh;
			mesh.SetRotation(ReticleTree.GroupBase.Alignment.RotaryRange, new AngularLength(0f, AngularLength.AngularUnit.DEG));
			// No range, no round: the gun on the line of sight, the emergency mark on the cross, each index on its zero.
			if (fcs.CurrentRange <= 0f || fcs.CurrentAmmoType == null)
			{
				mesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, default(AngularVector2));
				foreach (var index in RangeIndices)
				{
					mesh.SetAlignment(index.Align, default(AngularVector2));
				}
				return;
			}
			var offset = Offset(fcs);
			var forward = mesh.transform.forward;
			mesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, offset * forward, ReticleMesh.VectorType.Direction);
			var ballistics = BallisticComputerRepository.Instance;
			for (int i = 0; i < RangeIndices.Length; i++)
			{
				// The round's superelevation at the set range (as its scale's rows: VerticalBallistic), the scale's last
				// row at most, up from the zero row with the plate.
				float range = Mathf.Min(fcs.CurrentRange, _indexTops[i]);
				float lift = ballistics != null ? ballistics.GetSuperelevation(_indexRounds[i], range) : 0f;
				var up = Quaternion.AngleAxis(-lift, mesh.transform.right);
				mesh.SetAlignment(RangeIndices[i].Align, offset * up * forward, ReticleMesh.VectorType.Direction);
			}
		}

		// The gun's lay against the aim (superelevation, lead), applied to the line of sight. Not against the sight's own
		// direction: it lags at its limits while the turret comes round (the plate went off the screen). The parallax term
		// GHPC adds once a range is set (the sight's position less the muzzle's, GunnerFcs.Parallax) is taken back out.
		// GHPC also lays the gun by a fall of shot it interpolates in a straight line from its first 0.1 deg step, so the gun
		// stays at least 0.1 deg (1.78 mil) up out to about 1 km and the plate jumped that far at the first range in game
		// (1.78 mil from 100 to 1000 m, then the round's own 3.45 at 2000 m). The plate rises by the round's superelevation
		// instead (the table its scales are drawn from), but the lead stays the fire control's.
		internal static Quaternion Offset(FireControlSystem fcs)
		{
			var aim = fcs.AimWorldVector;
			var lay = fcs.CompensatedWeaponVector(fcs.SuperleadWeapon);
			var muzzle = fcs.LinkedWeaponSystems != null && fcs.LinkedWeaponSystems.Length > 0
				? fcs.LinkedWeaponSystems[0].MuzzleIdentity
				: null;
			if (ParallaxOn != null && muzzle != null && (bool)ParallaxOn.GetValue(fcs))
			{
				lay -= fcs.ReferenceTransform.position - muzzle.position;
			}
			var ballistics = BallisticComputerRepository.Instance;
			var round = LayRound?.GetValue(fcs) as AmmoType;
			float range = fcs.CurrentRange;
			if (ballistics != null && round != null && range > 0f)
			{
				float rise = Mathf.Tan(ballistics.GetSuperelevation(round, range) * Mathf.Deg2Rad) * range;
				lay += Vector3.up * (rise - ballistics.GetFallOfShot(round, range));
			}
			if (aim.sqrMagnitude < 1e-6f || lay.sqrMagnitude < 1e-6f) return Quaternion.identity;
			// Two look rotations, not FromToRotation: that one rounds angles under about 1 mrad to nothing (the plate stayed
			// on the cross out to 500 m).
			var toLay = Quaternion.LookRotation(lay.normalized, Vector3.up);
			return toLay * Quaternion.Inverse(Quaternion.LookRotation(aim.normalized, Vector3.up));
		}
	}
}
