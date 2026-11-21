using System.Collections.Generic;
using System.Linq;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using LeclercSXXI.Common;
using Reticle;
using TMPro;
using UnityEngine;

namespace LeclercSXXI
{
	// The HL 120 emergency gun-laying view (Sights.FitSecours): the commander's head slaved to the gun, laid by hand on a
	// French emergency reticle (Resources\hl120_secours.jsonc) with one range scale per round, as the Abrams' backup sight.
	// While it is the view, the fire control puts no range angle and no lead into the gun (the scale does that by eye).
	// The reticle follows the round selected.
	internal sealed class EmergencySight : MonoBehaviour
	{
		const string File = "hl120_secours.jsonc";
		const string RoundText = "#ROUND";

		UsableOptic _optic;
		FireControlSystem _fcs;
		ReticleSO _host, _fontHost;
		TMP_FontAsset _font;
		AmmoType _heat, _expl;
		readonly Dictionary<AmmoType, AmmoCodexScriptable> _codex = new Dictionary<AmmoType, AmmoCodexScriptable>();
		readonly Dictionary<AmmoType, ReticleSO> _built = new Dictionary<AmmoType, ReticleSO>();
		AmmoType _shown;
		bool _held, _wasElevating, _wasLeading;

		internal static void Attach(Vehicle vehicle, UsableOptic optic, FireControlSystem fcs)
		{
			optic.gameObject.AddComponent<EmergencySight>().Init(vehicle, optic, fcs);
		}

		void Init(Vehicle vehicle, UsableOptic optic, FireControlSystem fcs)
		{
			_optic = optic;
			_fcs = fcs;
			_host = optic.reticleMesh?.reticleSO;
			_fontHost = ReticleBuild.FontHost(_host, vehicle.transform);
			_font = new[]
			{
				ReticleBuild.Font("Reticle LiberationSans SDF"),
				ReticleBuild.Font("LiberationSans SDF"),
				ReticleBuild.FontTemplate(_fontHost)?.font }
				.FirstOrDefault(font => font != null && ReticleBuild.Missing(font, "OFL OECC EXPL SECOURS HM 0123456789").Length == 0);
			AmmoCodexScriptable First(AmmoClipCodexScriptable clip) => clip?.ClipType?.MinimalPattern?.FirstOrDefault();
			foreach (var clip in vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips ?? new AmmoClipCodexScriptable[0])
			{
				var round = First(clip);
				if (round?.AmmoType != null && !_codex.ContainsKey(round.AmmoType)) _codex[round.AmmoType] = round;
			}
			_heat = First(PortAmmo.Heat)?.AmmoType;
			_expl = First(PortAmmo.Extras.FirstOrDefault())?.AmmoType;
			optic.slot.ActiveChanged += ActiveChanged;
			var start = fcs.CurrentAmmoType ?? _codex.Keys.FirstOrDefault();
			Show(start, firstTime: true);
		}

		ReticleSO For(AmmoType ammo)
		{
			if (ammo == null || !_codex.TryGetValue(ammo, out var round) || _host == null || _fontHost == null) return null;
			if (_built.TryGetValue(ammo, out var reticle) && reticle != null) return reticle;
			int kind = ammo == _heat ? 1 : ammo == _expl ? 2 : 0;
			string key = kind == 1 ? "oecc" : kind == 2 ? "expl" : "ofl";
			reticle = Reticles.Secours.Build(File, _host, _fontHost, new Dictionary<string, AmmoCodexScriptable> { [key] = round },
				new Dictionary<string, double> { ["Round"] = kind });
			// ReticleMesh caches built meshes by name: one name per round.
			reticle.name = "Leclerc SXXI HL 120 secours (" + ammo.Name + ")";
			foreach (var label in ReticleBuild.AllOf<ReticleTree.Text>(reticle))
			{
				if (label.text == RoundText) label.text = ammo.Name;
				if (_font != null) label.font = _font;
			}
			return _built[ammo] = reticle;
		}

		void Show(AmmoType ammo, bool firstTime = false)
		{
			_shown = ammo;
			var reticle = For(ammo);
			var mesh = _optic.reticleMesh;
			if (reticle == null || mesh == null || mesh.reticleSO == reticle) return;
			mesh.reticleSO = reticle;
			if (firstTime)
			{
				ReticleBuild.PrepareCopy(mesh);
				return;
			}   // built on its first activation
			var width = GetComponent<ReticleWidth>();
			if (width != null)
			{
				width.Refresh();
				return;
			}   // rebuilds the mesh with this reticle's lines at the field's width
			ReticleMeshes.ClearSmr(mesh);
			mesh.Load();
		}

		void LateUpdate()
		{
			if (_fcs == null || _optic == null) return;
			var ammo = _fcs.CurrentAmmoType;
			if (ammo != _shown && _optic.slot.IsActive) Show(ammo);
			if (_held)
			{
				_fcs.SuperelevateWeapon = false;
				_fcs.SuperleadWeapon = false;
			}
		}

		void ActiveChanged(bool active)
		{
			if (_fcs == null) return;
			if (active && !_held)
			{
				_wasElevating = _fcs.SuperelevateWeapon;
				_wasLeading = _fcs.SuperleadWeapon;
				_held = true;
				_fcs.SuperelevateWeapon = _fcs.SuperleadWeapon = false;
				if (_fcs.CurrentAmmoType != _shown) Show(_fcs.CurrentAmmoType);
				// The lines keep the narrow field's width on the screen in the wide one. Attached at the first look: the copy
				// builds its mesh on its first activation.
				ReticleWidth.Attach(_optic, 1f);
			}
			else if (!active) Release();
		}

		void Release()
		{
			if (!_held || _fcs == null) return;
			_held = false;
			_fcs.SuperelevateWeapon = _wasElevating;
			_fcs.SuperleadWeapon = _wasLeading;
		}

		void OnDestroy()
		{
			Release();
			if (_optic != null && _optic.slot != null) _optic.slot.ActiveChanged -= ActiveChanged;
		}
	}
}
