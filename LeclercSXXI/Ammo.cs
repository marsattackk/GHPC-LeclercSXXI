using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;

namespace LeclercSXXI
{
	internal static class Ammo
	{
		// Only the 22 + 18 is sourced; the mix by round is a gameplay default
		static readonly int[] BustleCounts = { 12, 6, 4 };
		static readonly int[] DrumCounts = { 6, 8, 4 };

		const string Leo2 = "LEO2";
		// A NATO port's rounds come from NATO donors: the EXPL's look is the DM12A1's, never the T-80B's 3OF26
		const string Atlas120 = "120mm_de_co";
		const string ExplName = "120 EXPL F1 HE";

		internal static readonly AirBurst.Settings ExplFuze = new AirBurst.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			RoundName = ExplName,
			MenuName = "120 EXPL F1 fuze",
			MenuModes = new[] { AirBurst.Mode.Impact, AirBurst.Mode.Over },
			Default = AirBurst.Mode.Impact,
			Over = 3f,
			MinRange = 300f,
			TimeSd = 0f,
			Elements = 0,
		};

		internal static void Init() => PortAmmo.S = new PortAmmo.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			UnitKey = LeclercSXXIMod.UnitKey,
			Prefix = "leclercsxxi",
			SourceUnit = Leo2,
			// Pen and drag set as a pair: each dart meets its 2 km figure at 2 km in GHPC's decay.
			Apfsds = new[]
			{
				new PortAmmo.Round
				{
					Id = "OFLF2", Name = "OFL 120 F2 APFSDS", Source = "clip_DM23(120)", Shape = "OFL", Texture = "round_oflf2_co.png", Atlas = Atlas120,
					Set = a =>
					{
						a.RhaPenetration = 800f;
						a.MuzzleVelocity = 1740f;
						a.Mass = 4.5f;
						a.Coeff = 0.172f;
					},
					Prevalence = 1f,
				},
				new PortAmmo.Round
				{
					Id = "OFLF1", Name = "OFL 120 F1 APFSDS", Source = "clip_DM23(120)", Shape = "OFL", Texture = "round_oflf1_co.png", Atlas = Atlas120,
					Set = a =>
					{
						a.RhaPenetration = 696f;
						a.MuzzleVelocity = 1790f;
						a.Mass = 4.0f;
						a.Coeff = 0.148f;
					},
					Prevalence = 1f,
				},
			},
			DefaultApfsds = () => "OFLF2",
			ApfsdsListName = "120mm APFSDS (Leclerc SXXI)",
			MenuSuffix = " (Leclerc SXXI)",
			Heat = new PortAmmo.Round
			{
				Id = "OECC", Name = "OECC 120 F1 HEAT-MP", Source = "clip_DM12A1(120)", Texture = "round_oecc_co.png", Atlas = Atlas120,
				Set = a =>
				{
					a.MuzzleVelocity = 1100f;
					a.Mass = 14.4f;
				},
			},
			NoBaseHe = true,
			Extra = new[]
			{
				new PortAmmo.Round
				{
					Id = "EXPL", Name = ExplName, Source = "clip_3OF26", SourceUnit = "T80B", ModelFrom = "clip_DM12A1(120)", Shape = "EXPL", Texture = "round_expl_co.png", Atlas = Atlas120,
					Set = a =>
					{
						a.MuzzleVelocity = 1050f;
						a.Mass = 16.8f;
						a.TntEquivalentKg = 3.5f;
						a.Caliber = 120f;
						a.SectionalArea = 0.01131f;
					},
				},
			},
			TotalCounts = new[] { BustleCounts[0] + DrumCounts[0], BustleCounts[1] + DrumCounts[1], BustleCounts[2] + DrumCounts[2] },
			Racks = new[]
			{
				new PortAmmo.Rack { Index = 0, Counts = BustleCounts },
				new PortAmmo.Rack { Index = 1, Counts = DrumCounts },
			},
			DropOtherRacks = true,
		};

		internal static void Apply(Vehicle vehicle)
		{
			try
			{
				PortAmmo.ApplyAtSpawn(vehicle);
				RestockPriority.Apply(vehicle);
			}
			catch (System.Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} ammo setup failed: " + e); }
		}
	}
}
