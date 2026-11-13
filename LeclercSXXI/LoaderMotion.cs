using System;

namespace LeclercSXXI
{
	internal static class LoaderMotion
	{
		internal const int Capacity = 22;
		internal static double Wrap(double x) => x - Math.Floor(x / Capacity) * Capacity;
		internal static double Advance(double rank, double phase, int loadingRank) => Wrap(loadingRank - rank - phase);
		internal static double Ramp(double p, double start, double end)
		{
			double t = Math.Max(0, Math.Min(1, (p - start) / (end - start)));
			return t * t * (3 - 2 * t);
		}

		internal static double Index(double p) => Ramp(p, 0, 0.33);
		internal static double Door(double p) => Ramp(p, 0.34, 0.42) * (1 - Ramp(p, 0.90, 0.98));
		internal static double Gutter(double p) => Ramp(p, 0.34, 0.44) * (1 - Ramp(p, 0.80, 0.90));
		internal static double Ram(double p) => Ramp(p, 0.44, 0.64) * (1 - Ramp(p, 0.65, 0.80));
		internal static double Round(double p) => Ramp(p, 0.44, 0.64);
		internal static double Breech(double p) => Ramp(p, 0, 0.12) * (1 - Ramp(p, 0.91, 0.98));
	}
}
