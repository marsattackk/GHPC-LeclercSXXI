using System;

namespace LeclercSXXI
{
	// The endless conveyor: upper run travels left, lower run travels right. Phase is in cells.
	// The windows below (fractions of the 6 / 8 s reload) are my estimate, not measured: no timing of the real loader is published.
	internal static class LoaderMotion
	{
		internal const int Capacity = 22;
		internal static double Wrap(double value) => value - Math.Floor(value / Capacity) * Capacity;
		internal static double Advance(double rank, double phase, int loadingRank) => Wrap(loadingRank - rank - phase);
		internal static double Ramp(double progress, double start, double end)
		{
			double fraction = Math.Max(0, Math.Min(1, (progress - start) / (end - start)));
			return fraction * fraction * (3 - 2 * fraction);
		}
		internal static double Index(double progress) => Ramp(progress, 0, 0.33);
		internal static double Door(double progress) => Ramp(progress, 0.34, 0.42) * (1 - Ramp(progress, 0.90, 0.98));
		internal static double Gutter(double progress) => Ramp(progress, 0.34, 0.44) * (1 - Ramp(progress, 0.80, 0.90));
		internal static double Ram(double progress) => Ramp(progress, 0.44, 0.64) * (1 - Ramp(progress, 0.65, 0.80));
		internal static double Round(double progress) => Ramp(progress, 0.44, 0.64);
		internal static double Breech(double progress) => Ramp(progress, 0, 0.12) * (1 - Ramp(progress, 0.91, 0.98));
	}
}
