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
	[DefaultExecutionOrder(20)]
	internal sealed class GunnerDayReticle : MonoBehaviour
	{
		const string File = "hl130_day.jsonc";
		static readonly PortReticles Reticles = new PortReticles(typeof(GunnerDayReticle).Assembly, LeclercSXXIMod.FriendlyName);
		UsableOptic _optic;
		AmmoType[] _scaleAmmo;

		internal static ReticleSO Fit(Vehicle vehicle, UsableOptic optic, ReticleSO fallback)
		{
			var loadout = vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips;
			AmmoCodexScriptable FirstRound(AmmoClipCodexScriptable clip) => clip?.ClipType?.MinimalPattern?.FirstOrDefault();
			var ofl = loadout?.FirstOrDefault(clip => clip != null && PortAmmo.Apfsds.Values.Contains(clip));
			var rounds = new Dictionary<string, AmmoCodexScriptable>
			{
				["ofl"] = FirstRound(ofl),
				["oecc"] = FirstRound(PortAmmo.Heat),
				["expl"] = FirstRound(PortAmmo.Extras.FirstOrDefault()),
			};
			var host = optic.reticleMesh?.reticleSO;
			var fontHost = ReticleBuild.FontHost(host, vehicle.transform);
			var font = new[] { ReticleBuild.Font("Reticle LiberationSans SDF"), ReticleBuild.Font("LiberationSans SDF"),
				ReticleBuild.FontTemplate(fontHost)?.font }
				.FirstOrDefault(candidate => candidate != null && ReticleBuild.Missing(candidate, "OFL OECC EXPL 0123456789").Length == 0);
			var missing = rounds.Where(entry => entry.Value == null).Select(entry => entry.Key).ToArray();
			if (host == null || fontHost == null || font == null || missing.Length > 0)
			{
				MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName}: using the donor day reticle (host '{host?.name}', label template '{fontHost?.name}', usable font '{font?.name}', missing scales [{string.Join(", ", missing)}]).");
				return fallback;
			}

			var marks = Reticles.Build(File, host, fontHost, rounds);
			foreach (var label in ReticleBuild.AllOf<ReticleTree.Text>(marks)) label.font = font;
			var index = optic.gameObject.AddComponent<GunnerDayReticle>();
			index._optic = optic;
			index._scaleAmmo = rounds.Values.Select(round => round.AmmoType).ToArray();
			return marks;
		}

		void LateUpdate()
		{
			if (_optic?.slot == null || !_optic.slot.IsActive || _optic.FCS == null) return;
			var fcs = _optic.FCS;
			var ammo = fcs.CurrentAmmoType;
			var computer = BallisticComputerRepository.Instance;
			float elevation = computer != null && ammo != null && fcs.CurrentRange > 0f && _scaleAmmo.Contains(ammo)
				? computer.GetSuperelevation(ammo, fcs.CurrentRange) : 0f;
			_optic.reticleMesh.SetAlignment(ReticleTree.GroupBase.Alignment.Impact,
				new AngularVector2(0f, -elevation, AngularLength.AngularUnit.DEG));
		}
	}
}
