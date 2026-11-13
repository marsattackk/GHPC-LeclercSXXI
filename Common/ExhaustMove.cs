using System.Collections.Generic;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class ExhaustMove
	{
		internal struct Outlet
		{
			internal string Path;
			internal Vector3 Place;
			internal float Size;
			internal Outlet(string path, Vector3 place, float size = 1f) { Path = path; Place = place; Size = size; }
		}

		internal static void Place(Transform unit, IEnumerable<Outlet> outlets)
		{
			foreach (var o in outlets)
			{
				var emitter = LivePath.Find(unit, o.Path);
				if (emitter == null) continue;
				emitter.position = unit.TransformPoint(o.Place);
				if (!Mathf.Approximately(o.Size, 1f))
					foreach (var ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
					{
						var main = ps.main;
						main.startSizeMultiplier *= o.Size;
						main.startSpeedMultiplier *= o.Size;
					}
			}
		}
	}
}
