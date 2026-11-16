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
			internal Outlet(string path, Vector3 place, float size = 1f)
			{
				Path = path;
				Place = place;
				Size = size;
			}
		}

		internal static List<string> Place(Transform unit, IEnumerable<Outlet> outlets)
		{
			var lines = new List<string>();
			foreach (var outlet in outlets)
			{
				var emitter = LivePath.Find(unit, outlet.Path);
				if (emitter == null)
				{
					lines.Add($"no {outlet.Path}: that exhaust stays where the base has it");
					continue;
				}
				var was = unit.InverseTransformPoint(emitter.position);
				emitter.position = unit.TransformPoint(outlet.Place);
				int systems = 0;
				if (!Mathf.Approximately(outlet.Size, 1f))
					foreach (var particles in emitter.GetComponentsInChildren<ParticleSystem>(true))
					{
						var main = particles.main;
						main.startSizeMultiplier *= outlet.Size;
						main.startSpeedMultiplier *= outlet.Size;
						systems++;
					}
				lines.Add($"{outlet.Path}: {was} -> {outlet.Place}"
					+ (systems > 0 ? $", size x{outlet.Size} on {systems} particle systems" : ""));
			}
			return lines;
		}
	}
}
