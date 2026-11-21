using LeclercSXXI.Common;

namespace LeclercSXXI
{
	// The handles on the reticle files. The other tanks have one cache (All, Build, Spec). Here each class that builds from
	// the files keeps a cache of its own, so the day reticle and the display read the same file without sharing one.
	internal static class Reticles
	{
		internal static readonly PortReticles Hl130Day = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
		internal static readonly PortReticles Hl130Display = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
		internal static readonly PortReticles Secours = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
	}
}
