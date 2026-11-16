using LeclercSXXI.Common;

namespace LeclercSXXI
{
	// The handles on the reticle files. The other tanks have one cache (All, Build, Spec); here each class that builds from
	// the files keeps the cache of its own, as before the move, so the three stay apart: the day reticle and the display
	// read the same file through different caches.
	internal static class Reticles
	{
		internal static readonly PortReticles Hl130Day = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
		internal static readonly PortReticles Hl130Display = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
		internal static readonly PortReticles Secours = new PortReticles(typeof(Reticles).Assembly, LeclercSXXIMod.FriendlyName);
	}
}
