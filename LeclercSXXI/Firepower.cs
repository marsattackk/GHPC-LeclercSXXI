using GHPC.Vehicle;
using LeclercSXXI.Common;

namespace LeclercSXXI
{
	// The Leclerc's French 120 mm rounds. GHPC draws no French round: the stored models are the Leopard 2's NATO 120 x 570
	// one-piece rounds (it is a NATO tank), repainted and reshaped as the French rounds. Every round has a name of its own
	// (GHPC compares rounds by name: a vanilla name would join that round's lists).
	//   OFL 120 F2   APFSDS, depleted uranium, the usual dart: the DM23 (120) cloned, its values ours
	//   OFL 120 F1   APFSDS, tungsten (the DM43 rod), the second choice in the APFSDS list
	//   OECC 120 F1  HEAT multipurpose: the DM12A1 cloned (its 560 mm borrowed), the French round's mass and speed
	//   120 EXPL F1  HE: the T-80B's 3OF26 HE behaviour (fragments, impact fuse) on the DM12A1's NATO model, the EXPL's
	//                mass, speed and charge; the vanilla HE's game penetration
	// APFSDS penetration (on the T-90A 3BM60's 60 deg footing): GHPC muzzle RhaPenetration = LOS at 2 km / f, f = (v2000 /
	// v0)^2, the drag Coeff set so the round loses its 100 m/s by 2 000 m (with the DM23's sectional area 0.00062 m2, air 1.25).
	internal static partial class Firepower
	{
		// 40 = 22 in the bustle + 18 in the drum (French army: «40 obus dont 22 coups prêts à l'emploi»). Only the 22 + 18 is
		// sourced; the split by round (APFSDS, HEAT, HE) is a gameplay default of mine: 18 / 14 / 8.
		static readonly PortAmmo.Mix BustleCounts = new PortAmmo.Mix { Apfsds = 12, Heat = 6, Extra = new[] { 4 } };   // HE: the EXPL
		static readonly PortAmmo.Mix DrumCounts = new PortAmmo.Mix { Apfsds = 6, Heat = 8, Extra = new[] { 4 } };
		static readonly PortAmmo.Mix Counts = new PortAmmo.Mix
		{
			Apfsds = BustleCounts.Apfsds + DrumCounts.Apfsds,
			Heat = BustleCounts.Heat + DrumCounts.Heat,
			Extra = new[] { BustleCounts.Extra[0] + DrumCounts.Extra[0] },
		};

		// The M2HB coax's belt: 950 rounds (LeclercSXXIMod.CoaxRounds) in belts of 140, six full and one of 110, the maker's figure.
		internal const int M2Belt = 140;

		const string Leo2 = "LEO2";   // registry key of the vanilla LEO2A0 (its list: clip_DM23(120), clip_DM12A1(120))
		// The stored rounds' look: each round's copy of the DM23 / DM12A1 atlas, repainted as the French photos show them
		// (white cases with black bands, French stencils); the OFL's longer dart and the EXPL's ogive as reshaped meshes.
		// A NATO port's rounds come from NATO donors: the EXPL's look is the DM12A1's, never the T-80B's 3OF26.
		const string Atlas120 = "120mm_de_co";
		const string ExplName = "120 EXPL F1 HE";

		// Impact follows the maker's HE F1 catalogue. The selectable overhead burst is a gameplay approximation.
		internal static readonly AirBurst.Settings Fuse = new AirBurst.Settings
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

		internal static PortAmmo.Settings Settings() => new PortAmmo.Settings
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
					Id = "OFLF2",
					Name = "OFL 120 F2 APFSDS",
					Source = "clip_DM23(120)",
					Shape = "OFL",
					Texture = "round_oflf2_co.png",
					Atlas = Atlas120,
					Set = ammo =>
					{
						// ~800 = the F1's 696 + 15 %. Jane's 1999-2000 has the F2 at 640 mm at 2 km against the F1's 560 (about 14 %
						// better, as the English Wikipedia's 120x570 article quotes it); inferred, no trial figure published.
						ammo.RhaPenetration = 800f;
						ammo.MuzzleVelocity = 1740f;   // «1 740 m/s» (French Wikipedia, CN120-26)
						ammo.Mass = 4.5f;              // the dart (French Wikipedia, CN120-26)
						// Derived: 1 740 -> 1 640 m/s at 2 000 m (the F1's 100 m/s loss, inferred) with this mass and area.
						ammo.Coeff = 0.172f;
					},
					Prevalence = 1f,   // gameplay setting of mine, no source
				},
				new PortAmmo.Round
				{
					Id = "OFLF1",
					Name = "OFL 120 F1 APFSDS",
					Source = "clip_DM23(120)",
					Shape = "OFL",
					Texture = "round_oflf1_co.png",
					Atlas = Atlas120,
					Set = ammo =>
					{
						// Lanz-Odermatt on the DM43 rod (574 x 20.3 mm W, Kotsch) 621 mm LOS at 60 deg at 2 km, / f 0.891 = 696 at
						// the muzzle; derived.
						ammo.RhaPenetration = 696f;
						ammo.MuzzleVelocity = 1790f;   // maker «1 790 m/s (obus flèche)» (GIAT, Supériorité opérationnelle)
						ammo.Mass = 4.0f;              // the dart (French Wikipedia, CN120-26)
						// Derived: 1 790 -> 1 690 m/s at 2 000 m (English Wikipedia, 120x570) with this mass and area.
						ammo.Coeff = 0.148f;
					},
					Prevalence = 1f,   // gameplay setting of mine, no source
				},
			},
			DefaultApfsds = () => LeclercSXXIMod.Apfsds?.Value,
			ForceApfsds = () => LeclercSXXIMod.ForceApfsds?.Value == true,
			ApfsdsListName = "120mm APFSDS (Leclerc SXXI)",
			MenuSuffix = " (Leclerc SXXI)",
			Heat = new PortAmmo.Round
			{
				Id = "OECC",
				Name = "OECC 120 F1 HEAT-MP",
				Source = "clip_DM12A1(120)",
				Texture = "round_oecc_co.png",
				Atlas = Atlas120,
				Set = ammo =>
				{
					// 560 mm: GHPC's DM12A1 as it is (borrowed: the French figure «480» has no known origin, French Wikipedia,
					// CN120-26); constant with range (shaped charge). Filler: the DM12A1's 2.21 kg TNT, no source.
					ammo.MuzzleVelocity = 1100f;   // «vitesse initiale de 1 100 m/s» (French Wikipedia, Leclerc)
					ammo.Mass = 14.4f;             // the projectile (French Wikipedia, Leclerc; leclerc.fr's ammunition page)
				},
			},
			NoBaseHe = true,   // the T-80B's Soviet 3OF26 out of the list; the EXPL takes the HE key
			Extra = new[]
			{
				new PortAmmo.Round
				{
					Id = "EXPL",
					Name = ExplName,
					Source = "clip_3OF26",
					SourceUnit = "T80B",
					ModelFrom = "clip_DM12A1(120)",
					Shape = "EXPL",
					Texture = "round_expl_co.png",
					Atlas = Atlas120,
					Set = ammo =>
					{
						// The 3OF26's game penetration (80 mm), fragments, impact fuse and drag Coeff kept (game convention).
						// AirBurst adds the optional timed burst per shot using these HE effects.
						ammo.MuzzleVelocity = 1050f;   // maker (Nexter catalogue 2018, p. 34)
						ammo.Mass = 16.8f;             // the projectile, maker (Nexter catalogue 2018, p. 34; Cavalerie 2016: 16 kg)
						// 3 kg Compo B (Cavalerie 2016) x 1.17 (the vanilla 3OF26's own TNT to filler ratio, 3.97 / 3.4; inferred)
						// = 3.5 kg TNT.
						ammo.TntEquivalentKg = 3.5f;
						ammo.Caliber = 120f;
						ammo.SectionalArea = 0.01131f;   // derived: a 120 mm circle (the 3OF26's was its 125 mm one)
					},
				},
			},
			TotalCounts = Counts,
			// Restock order: the bustle conveyor first (GHPC refills the first rack with room), the drum last (it gives).
			Racks = new[]
			{
				// The T-80B carousel, now the bustle conveyor (Conversion.Interior.cs).
				new PortAmmo.Rack { Index = 0, Counts = BustleCounts },
				// The T-80B hull wet rack, now the drum.
				new PortAmmo.Rack { Index = 1, Counts = DrumCounts },
			},
			DropOtherRacks = true,
		};

		// At spawn, after the interior (its racks placed), before LoadoutManager.Start fills them.
		internal static void Apply(Vehicle vehicle)
		{
			PortAmmo.ApplyAtSpawn(vehicle);
			RestockPriority.Apply(vehicle);
		}
	}
}
