using System.Linq;
using UnityEngine;

namespace LeclercSXXI.Common
{
	// GHPC turns some back on (lamps, damage states).
	public class KeepHidden : MonoBehaviour
	{
		internal Renderer[] Renderers = new Renderer[0];
		internal Light[] Lights = new Light[0];

		void LateUpdate()
		{
			foreach (var r in Renderers) if (r != null && r.enabled) r.enabled = false;
			foreach (var l in Lights) if (l != null && l.enabled) l.enabled = false;
		}

		public static void HideGear(Transform root, string[] names, string ownPrefix)
		{
			var rs = Enumerable.ToArray(Enumerable.Where(root.GetComponentsInChildren<Renderer>(true),
				r => System.Array.IndexOf(names, r.gameObject.name) >= 0 && !r.name.StartsWith(ownPrefix)));
			foreach (var r in rs) r.enabled = false;
			Add(root, rs, null);
		}

		public static void Add(Transform root, Renderer[] renderers, Light[] lights)
		{
			var keep = root.gameObject.AddComponent<KeepHidden>();
			keep.Renderers = renderers ?? new Renderer[0];
			keep.Lights = lights ?? new Light[0];
		}
	}
}
