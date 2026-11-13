using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC.Weaponry;
using Reticle;
using TMPro;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class ReticleBuild
	{
		static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

		internal static ReticleSO Build(ReticleSpec spec, ReticleSO host, ReticleTree.Text font, Func<string, AmmoCodexScriptable> ammo, List<string> problems)
		{
			var so = ScriptableObject.CreateInstance<ReticleSO>();
			so.name = spec.Name;
			problems.AddRange(spec.Problems);
			if (spec.LightsFromHost && host != null)
				so.lights = host.lights.Select(l => new ReticleTree.Light { type = l.type, color = Set(spec, l.type) ?? l.color }).ToList();
			foreach (var kv in spec.Lights)
			{
				if (!Enum.TryParse(kv.Key, out ReticleTree.Light.Type t)) { problems.Add($"lights: unknown light '{kv.Key}'"); continue; }
				if (!so.lights.Any(l => l.type == t)) so.lights.Add(new ReticleTree.Light { type = t, color = Rgb(kv.Value) });
			}
			// GHPC ignores Second, every reticle draws in the first focal plane.
			var plane = new ReticleTree.FocalPlane(spec.Plane == "second" ? ReticleTree.FocalPlane.Type.Second : ReticleTree.FocalPlane.Type.First);
			plane.elements = new List<ReticleTree.TransformElement>();
			so.planes.Add(plane);
			var ctx = new Ctx { Font = font, Ammo = ammo };
			foreach (var n in spec.Top)
			{
				var e = Make(n, null, false, ctx, problems);
				if (e != null) plane.elements.Add(e);
			}
			foreach (var light in AllOf<ReticleTree.VisualElement>(so).Select(v => v.illumination).Distinct())
				if (light != ReticleTree.Light.Type.None && !so.lights.Any(l => l.type == light))
					problems.Add($"elements lit by '{light}', which the reticle has no colour for");
			foreach (var u in Unaligned(so)) problems.Add("still without an alignment: " + u);
			return so;
		}

		// A copied reticle mesh keeps the old built mesh (Load then builds nothing) and the old light states by position.
		internal static void PrepareCopy(ReticleMesh rm)
		{
			if (rm == null || rm.reticleSO == null) return;
			var smr = rm.GetComponent<SkinnedMeshRenderer>();
			if (smr != null) smr.sharedMesh = null;
			rm.lights = rm.reticleSO.lights.Select(l => new ReticleEditor.LightState(l, l.type == ReticleTree.Light.Type.NightIllumination ? 0f : 1f)).ToList();
		}

		static RGB? Set(ReticleSpec spec, ReticleTree.Light.Type t)
		{
			foreach (var kv in spec.Lights) if (kv.Key == t.ToString()) return Rgb(kv.Value);
			return null;
		}

		static RGB Rgb(float[] c) => new RGB(c[0], c[1], c[2]);

		sealed class Ctx
		{
			internal ReticleTree.Text Font;
			internal Func<string, AmmoCodexScriptable> Ammo;
			internal bool FontMissingReported;
		}

		static ReticleTree.TransformElement Make(ReticleSpec.Node n, ReticleTree.GroupBase parent, bool aligned, Ctx ctx, List<string> problems)
		{
			var pos = new Vector2();
			if (n is ReticleSpec.Group g)
			{
				var align = ReticleTree.GroupBase.Alignment.None;
				if (g.Align != null && !Enum.TryParse(g.Align, out align)) { problems.Add($"{g.Path}: unknown alignment '{g.Align}' (group left out)"); return null; }
				pos = new Vector2((float)g.X, (float)g.Y);
				ReticleTree.GroupBase grp;
				if (g.Ammo != null)
				{
					var round = ctx.Ammo?.Invoke(g.Ammo);
					if (round == null) { problems.Add($"{g.Path}: no round for '{g.Ammo}' (ballistic group left out)"); return null; }
					var vb = new ReticleTree.VerticalBallistic(pos, parent, align) { projectile = round };
					vb.UpdateBC();
					grp = vb;
				}
				else if (g.Stadia > 0) grp = new ReticleTree.Stadia(pos, parent, align) { targetSize = new LinearLength((float)g.Stadia) };
				else grp = new ReticleTree.Angular(pos, parent, align) { name = g.Name };
				bool under = aligned || align != ReticleTree.GroupBase.Alignment.None;
				foreach (var c in g.Children)
				{
					var e = Make(c, grp, under, ctx, problems);
					if (e != null) grp.elements.Add(e);
				}
				return grp;
			}
			// Outside an aligned group ReticleMesh gives it bone -1, which hides the whole reticle without a word.
			if (!aligned)
			{
				problems.Add($"{n.Path}: not under a group with an alignment (it would hide the whole reticle; left out)");
				return null;
			}
			switch (n)
			{
				case ReticleSpec.Line l:
				{
					var line = new ReticleTree.Line(new Vector2((float)l.X, (float)l.Y), (float)l.Deg, (float)l.Len, (float)l.W, 1f, parent);
					line.length.unit = AngularLength.AngularUnit.MIL_USSR;
					line.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
					return Look(line, l.Visual, l.Light, l.Path, problems);
				}
				case ReticleSpec.Circle c:
				{
					var circle = new ReticleTree.Circle(new Vector2((float)c.X, (float)c.Y), 0f, (float)c.R, (float)c.W, c.Segments, parent);
					return Look(circle, c.Visual, c.Light, c.Path, problems);
				}
				case ReticleSpec.Text t:
				{
					if (ctx.Font == null)
					{
						if (!ctx.FontMissingReported) problems.Add($"{t.Path}: no reticle font to clone; labels left out");
						ctx.FontMissingReported = true;
						return null;
					}
					var label = (ReticleTree.Text)Clone.Invoke(ctx.Font, null);
					label.position = new ReticleTree.Position((float)t.X, (float)t.Y, AngularLength.AngularUnit.MIL_USSR);
					label.rotation = new AngularLength(0f, AngularLength.AngularUnit.DEG);
					label.parent = parent;
					label.text = t.Value;
					label.fontSize = (float)t.Size;
					return Look(label, t.Visual, t.Light, t.Path, problems);
				}
			}
			return null;
		}

		static ReticleTree.VisualElement Look(ReticleTree.VisualElement v, string visual, string light, string path, List<string> problems)
		{
			if (visual == "painted") v.visualType = ReticleTree.VisualElement.Type.Painted;
			else if (visual == "additive") v.visualType = ReticleTree.VisualElement.Type.ReflectedAdditive;
			else problems.Add($"{path}: unknown visual '{visual}' (painted, additive)");
			if (Enum.TryParse(light, out ReticleTree.Light.Type t)) v.illumination = t;
			else problems.Add($"{path}: unknown light '{light}'");
			return v;
		}

		internal static List<string> Unaligned(ReticleSO so)
		{
			var bad = new List<string>();
			void Walk(ReticleTree.TransformElement e, bool aligned, string path)
			{
				if (e is ReticleTree.GroupBase g)
				{
					bool under = aligned || g.align != ReticleTree.GroupBase.Alignment.None;
					foreach (var c in g.elements) Walk(c, under, path + "/" + g);
				}
				else if (e is ReticleTree.VisualElement && !aligned) bad.Add(path + "/" + e);
			}
			foreach (var p in so.planes) foreach (var e in p.elements) Walk(e, false, so.name);
			return bad;
		}

		internal static ReticleTree.Text FontTemplate(ReticleSO host)
		{
			if (host == null) return null;
			var labels = AllOf<ReticleTree.Text>(host).ToList();
			return labels.FirstOrDefault(t => t.font != null && t.font.name.StartsWith("TPD_Etch")) ?? labels.FirstOrDefault();
		}

		internal static ReticleSO FontHost(ReticleSO own, Transform unit)
		{
			bool Labelled(ReticleSO so) => so != null && so.planes != null && AllOf<ReticleTree.Text>(so).Any(t => t.font != null);
			if (Labelled(own)) return own;
			var onUnit = unit == null ? null : unit.GetComponentsInChildren<GHPC.Equipment.Optics.UsableOptic>(true).Select(o => o.reticleMesh != null ? o.reticleMesh.reticleSO : null).FirstOrDefault(Labelled);
			if (onUnit != null) return onUnit;
			var loaded = Resources.FindObjectsOfTypeAll<ReticleSO>().Where(Labelled).OrderBy(so => so.name, StringComparer.Ordinal).ToList();
			return loaded.FirstOrDefault(so => FontTemplate(so)?.font?.name.StartsWith("TPD_Etch") == true) ?? loaded.FirstOrDefault();
		}

		internal static IEnumerable<T> AllOf<T>(ReticleSO so) where T : ReticleTree.TransformElement
		{
			var stack = new Stack<ReticleTree.TransformElement>(so.planes.SelectMany(p => p.elements));
			while (stack.Count > 0)
			{
				var e = stack.Pop();
				if (e is T match) yield return match;
				if (e is ReticleTree.GroupBase g) foreach (var c in g.elements) stack.Push(c);
			}
		}

		internal static string Missing(TMP_FontAsset font, string text)
		{
			if (font == null || string.IsNullOrEmpty(text)) return "";
			string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "").Replace(" ", "");
			if (plain.Length == 0 || font.HasCharacters(plain, out uint[] missing, true, true)) return "";
			return new string((missing ?? new uint[0]).Distinct().Select(c => (char)c).ToArray());
		}

		static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>();

		internal static TMP_FontAsset Font(string name)
		{
			if (Fonts.TryGetValue(name, out var f) && f != null) return f;
			// Loaded fonts only, one that lives in a vehicle bundle turns up with its vehicle.
			f = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(x => x.name == name) ?? Resources.LoadAll<TMP_FontAsset>("").FirstOrDefault(x => x.name == name);
			if (f != null) Fonts[name] = f;
			return f;
		}
	}
}
