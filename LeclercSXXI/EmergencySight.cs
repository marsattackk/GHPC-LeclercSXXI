using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	internal sealed class EmergencySight : MonoBehaviour
	{
		const string File = "hl120_secours.jsonc";
		const string RoundText = "#ROUND";
		static readonly PortReticles Reticles = new PortReticles(typeof(EmergencySight).Assembly, LeclercSXXIMod.FriendlyName);
		static readonly FieldInfo Smr = Reflect.Field(typeof(ReticleMesh), "SMR");

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
			var e = optic.gameObject.AddComponent<EmergencySight>();
			e._optic = optic;
			e._fcs = fcs;
			e._host = optic.reticleMesh?.reticleSO;
			e._fontHost = ReticleBuild.FontHost(e._host, vehicle.transform);
			e._font = new[] { ReticleBuild.Font("Reticle LiberationSans SDF"), ReticleBuild.Font("LiberationSans SDF"), ReticleBuild.FontTemplate(e._fontHost)?.font }
				.FirstOrDefault(f => f != null && ReticleBuild.Missing(f, "OFL OECC EXPL SECOURS HM 0123456789").Length == 0);
			AmmoCodexScriptable First(AmmoClipCodexScriptable clip) => clip?.ClipType?.MinimalPattern?.FirstOrDefault();
			foreach (var clip in vehicle.GetComponent<LoadoutManager>()?.LoadedAmmoList?.AmmoClips ?? new AmmoClipCodexScriptable[0])
			{
				var round = First(clip);
				if (round?.AmmoType != null && !e._codex.ContainsKey(round.AmmoType)) e._codex[round.AmmoType] = round;
			}
			e._heat = First(PortAmmo.Heat)?.AmmoType;
			e._expl = First(PortAmmo.Extras.FirstOrDefault())?.AmmoType;
			optic.slot.ActiveChanged += e.ActiveChanged;
			var start = fcs.CurrentAmmoType ?? e._codex.Keys.FirstOrDefault();
			e.Show(start, firstTime: true);
		}

		ReticleSO For(AmmoType ammo)
		{
			if (ammo == null || !_codex.TryGetValue(ammo, out var round) || _host == null || _fontHost == null) return null;
			if (_built.TryGetValue(ammo, out var so) && so != null) return so;
			int kind = ammo == _heat ? 1 : ammo == _expl ? 2 : 0;
			string key = kind == 1 ? "oecc" : kind == 2 ? "expl" : "ofl";
			so = Reticles.Build(File, _host, _fontHost, new Dictionary<string, AmmoCodexScriptable> { [key] = round },
				new Dictionary<string, double> { ["Round"] = kind });
			so.name = "Leclerc SXXI HL 120 secours (" + ammo.Name + ")";
			foreach (var label in ReticleBuild.AllOf<ReticleTree.Text>(so))
			{
				if (label.text == RoundText) label.text = ammo.Name;
				if (_font != null) label.font = _font;
			}
			return _built[ammo] = so;
		}

		void Show(AmmoType ammo, bool firstTime = false)
		{
			_shown = ammo;
			var so = For(ammo);
			var mesh = _optic.reticleMesh;
			if (so == null || mesh == null || mesh.reticleSO == so) return;
			mesh.reticleSO = so;
			if (firstTime) { ReticleBuild.PrepareCopy(mesh); return; }
			Smr.SetValue(mesh, null);
			mesh.Load();
		}

		void LateUpdate()
		{
			if (_fcs == null || _optic == null) return;
			var ammo = _fcs.CurrentAmmoType;
			if (ammo != _shown && _optic.slot.IsActive) Show(ammo);
			if (_held) { _fcs.SuperelevateWeapon = false; _fcs.SuperleadWeapon = false; }
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
