using System.Collections.Generic;
using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using LeclercSXXI.Common;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace LeclercSXXI
{
	// The HL 130 day reticle (Resources\hl130_day.jsonc, drawn from pictures of the real sight): the nominal cross on the line
	// of sight, the plate (drop scales, stadia, mil scale, emergency mark) moved by the gun's lay against it. The pictures put
	// the plate 7.6 to 23.6 mil above the cross and once 10 mil to its side while the cross and display box keep their place: inferred, the
	// plate shows the gun's direction (the fire control's lay: superelevation and lead; not the barrel itself, so the loading
	// dip doesn't drag it). Not the parallax: the gun still converges on the line of sight, but the plate jumped 5.6 mil
	// sideways at 100 m, 1.9 at 300 m (the head 0.56 m beside the bore), at every range step (user, 2026-11-16).
	[DefaultExecutionOrder(20)]
	internal sealed class GunnerDayReticle : MonoBehaviour
	{
		const string File = "hl130_day.jsonc";
		static readonly System.Reflection.FieldInfo ParallaxOn = Reflect.Field(typeof(FireControlSystem), "_fixParallaxForVectorMode");
		UsableOptic _optic;

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
				MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName}: using the donor day reticle (host '{host?.name}', "
					+ $"label template '{fontHost?.name}', usable font '{font?.name}', "
					+ $"missing scales [{string.Join(", ", missing)}]).");
				return fallback;
			}

			var marks = Reticles.Hl130Day.Build(File, host, fontHost, rounds);
			foreach (var label in ReticleBuild.AllOf<ReticleTree.Text>(marks)) label.font = font;
			optic.gameObject.AddComponent<GunnerDayReticle>().Init(optic);
			return marks;
		}

		void Init(UsableOptic optic)
		{
			_optic = optic;
		}

		void LateUpdate()
		{
			if (_optic?.slot == null || !_optic.slot.IsActive || _optic.FCS == null || _optic.reticleMesh == null) return;
			var fcs = _optic.FCS;
			// No range, no round: the gun on the line of sight, the emergency mark on the cross.
			if (fcs.CurrentRange <= 0f || fcs.CurrentAmmoType == null)
			{
				_optic.reticleMesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, default(AngularVector2));
				return;
			}
			var offset = Offset(fcs);
			var mesh = _optic.reticleMesh;
			mesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact, offset * mesh.transform.forward, ReticleMesh.VectorType.Direction);
		}

		// The gun's lay against the aim (superelevation, lead), applied to the line of sight. Not against the sight's own
		// direction: it lags at its limits while the turret comes round (the plate went off the screen). The parallax term
		// GHPC adds once a range is set (the sight's position less the muzzle's, GunnerFcs.Parallax) is taken back out.
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
			if (aim.sqrMagnitude < 1e-6f || lay.sqrMagnitude < 1e-6f) return Quaternion.identity;
			return Quaternion.FromToRotation(aim.normalized, lay.normalized);
		}
	}
}
