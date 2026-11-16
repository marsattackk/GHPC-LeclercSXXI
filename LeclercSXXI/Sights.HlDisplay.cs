using System.Collections.Generic;
using System.Globalization;
using GHPC.Equipment.Optics;
using GHPC.Weapons;
using LeclercSXXI.Common;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LeclercSXXI
{
	// The HL 130's display (from the same pictures): the day box under the cross (Resources\hl130_day.jsonc) and the thermal row
	// (hl130_thermal.jsonc). The range in metres, «HF» when none is set (02, 04), the round's letter, the ready-to-fire mark,
	// and the hull's direction (hl130_chassis.jsonc) turned by the hull's angle to the turret.
	internal sealed partial class HlDisplay : MonoBehaviour
	{
		const string Tag = LeclercSXXIMod.FriendlyName;
		const string ChassisFile = "hl130_chassis.jsonc";
		const float RangeStep = 5f;   // 04 reads 0775 and 0760

		FireControlSystem _fcs;
		TextMeshProUGUI _range, _hf;
		RectTransform _hull;
		Transform _turret;
		float _shownRange = -1f;

		// The round's letter: 09 shows «F» in its box, 04 «C» (an OECC fired). F flèche (OFL), C charge creuse (OECC);
		// E explosif for the HE round is inferred (no picture shows it).
		static string Letter(AmmoType ammo) =>
			ammo.Caliber < 20f ? ""
			: ammo.Category == AmmoType.AmmoCategory.Penetrator ? "F"
			: ammo.Category == AmmoType.AmmoCategory.ShapedCharge ? "C"
			: "E";

		// Builds the display file's screen items under the optic and binds them; tint colours the hull symbol (drawn white).
		internal static void Attach(UsableOptic optic, string file, Transform turret, Color tint)
		{
			var problems = new List<string>();
			var spec = Reticles.Hl130Display.Spec(file);
			float fov = (float)spec.Fov;
			var overlay = ReticleBuild.BuildOverlay(spec, optic.transform, fov, problems);
			var settings = new SightReadout.Settings { Ammo = "ammo", AmmoText = Letter };
			SightReadout.Attach(optic, overlay, settings, problems);
			var display = overlay.Root.AddComponent<HlDisplay>();
			// The text colour is the description's: a font whose material has its own face colour (the 7-segment font's is red)
			// gets a white one.
			foreach (var label in overlay.Readouts.Values)
			{
				var material = label.fontMaterial;   // an instance: the font's shared material stays as it is
				if (material.HasProperty(ShaderUtilities.ID_FaceColor)) material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
				material.DisableKeyword(ShaderUtilities.Keyword_Glow);
				material.DisableKeyword(ShaderUtilities.Keyword_Underlay);
			}
			overlay.Readouts.TryGetValue("range", out var range);
			overlay.Readouts.TryGetValue("hf", out var noRange);
			if (range == null || noRange == null) problems.Add("no 'range' / 'hf' readout");
			var ready = overlay.Root.transform.Find("ready");
			if (ready != null)
			{
				optic.ReadyToFireObject = ready.gameObject;   // GHPC shows it while the gun is ready to fire
				ready.gameObject.SetActive(false);
			}
			else problems.Add("no 'ready' mark");
			var hull = Hull(spec, optic.transform, fov, tint, problems);
			display.Init(optic.FCS, turret, range, noRange, hull);
			foreach (var problem in problems) MelonLogger.Warning($"{Tag}: {file}: {problem}.");
		}

		void Init(FireControlSystem fcs, Transform turret, TextMeshProUGUI range, TextMeshProUGUI noRange, RectTransform hull)
		{
			_fcs = fcs;
			_turret = turret;
			_range = range;
			_hf = noRange;
			_hull = hull;
		}

		// The hull symbol, drawn 1 unit tall, built at the display's HullX / HullY / HullH (mrad on its screen) inside a pivot
		// the display turns.
		static RectTransform Hull(ReticleSpec display, Transform parent, float fov, Color tint, List<string> problems)
		{
			var spec = Reticles.Hl130Display.Spec(ChassisFile);
			float height = (float)display.Num("HullH");
			var overlay = ReticleBuild.BuildOverlay(spec, parent, fov / height, problems);
			var pivot = (RectTransform)new GameObject("hull pivot", typeof(RectTransform)).transform;
			pivot.SetParent(overlay.Root.transform, false);
			pivot.anchorMin = pivot.anchorMax = pivot.pivot = new Vector2(0.5f, 0.5f);
			pivot.sizeDelta = Vector2.zero;
			float canvasPerMrad = 600f / (fov * Mathf.Deg2Rad * 1000f);   // as ReticleBuild.BuildOverlay
			pivot.anchoredPosition = new Vector2((float)display.Num("HullX"), (float)display.Num("HullY")) * canvasPerMrad;
			var strokes = new List<Transform>();
			foreach (Transform child in overlay.Root.transform)
				if (child != pivot) strokes.Add(child);
			foreach (var stroke in strokes)
			{
				stroke.SetParent(pivot, false);
				var image = stroke.GetComponent<Image>();
				if (image != null) image.color = new Color(tint.r, tint.g, tint.b, image.color.a);
			}
			if (strokes.Count == 0) problems.Add($"{ChassisFile}: no strokes");
			return pivot;
		}

		void LateUpdate()
		{
			if (_fcs == null) return;
			float range = _fcs.CurrentRange;
			bool fixedSight = range < 1f || Mathf.Approximately(range, _fcs.DefaultRange);
			float shown = fixedSight ? 0f : Mathf.Round(range / RangeStep) * RangeStep;
			if (shown != _shownRange && _range != null && _hf != null)
			{
				_shownRange = shown;
				_hf.gameObject.SetActive(fixedSight);
				_range.gameObject.SetActive(!fixedSight);
				if (!fixedSight) _range.text = shown.ToString("0000", CultureInfo.InvariantCulture);
			}
			if (_hull != null && _turret != null && _turret.parent != null)
			{
				var hull = _turret.parent;
				var turretAhead = Vector3.ProjectOnPlane(_turret.forward, hull.up);
				// The turret turned right of the hull: the hull lies to the left on the display (counter-clockwise).
				float angle = Vector3.SignedAngle(hull.forward, turretAhead, hull.up);
				_hull.localRotation = Quaternion.Euler(0f, 0f, angle);
			}
		}
	}
}
