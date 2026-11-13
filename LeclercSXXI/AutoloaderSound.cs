using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;

namespace LeclercSXXI
{
	internal static class AutoloaderSound
	{
		static ReloadSound.Settings Cycle(int seconds) => new ReloadSound.Settings {
			Sound = new GunShotSound.Settings {
				ModName = LeclercSXXIMod.FriendlyName, What = "autoloader sound",
				InteriorResource = $"reload_{seconds}_interior.wav",
				ExteriorResource = $"reload_{seconds}_exterior.wav",
				MinDistance = 3f, MaxDistance = 6f,
				BusPath = "bus:/InteriorExterior/Effects",
			},
			ClipSeconds = seconds, SyncTolerance = .04f, MaxHearDistance = 6f,
			Rolloff = FMOD.MODE._3D_LINEARSQUAREROLLOFF,
			Position = feed => feed.Carousel != null ? feed.Carousel.transform.position : feed.transform.position,
		};
		internal static void Apply(Vehicle vehicle)
		{
			var gun = vehicle.GetComponent<WeaponsManager>()?.Weapons[0]?.Weapon;
			var settings = Cycle(6);
			settings.Variants = new[] { Cycle(6), Cycle(8) };
			ReloadSound.Attach(settings, gun, vehicle);
		}
	}
}
