using GHPC.Vehicle;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// The CN120-26 «120 mm modèle F1» on the T-80B's mount: drives, limits, the rear-deck depression, the autoloader's cycle
	// by round type and the HUD name (shared GunSetup).
	internal static class FirepowerGun
	{
		static readonly GunSetup.Settings Settings = new GunSetup.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			// «vitesse de déplacement de 40°/s en azimut et le pointage en site de 30°/s» (French Wikipedia, Leclerc)
			TurretSpeed = 40f,
			GunSpeed = 30f,
			// «Elevation (+20°), depression (-8°)» (Forecast International, p. 3; English Wikipedia, Leclerc)
			GunLimits = new Vector2(-8f, 20f),
			// Over the rear deck: the barrel 1 cm over the model's hull (-1.6 deg at worst, 166 deg right of the nose): model
			// clearance, inferred (no source gives the rear limit). The table is Resources\gun_depression.txt.
			DepressionProfile = "gun_depression.txt",
			// «séquence de tir nominale est inférieure à huit secondes, et le temps de redoublement (charger deux munitions du
			// même type l'une après l'autre) inférieur à six secondes» (French Wikipedia, Leclerc, after Ferrard & Turbé 1992).
			// Conveyor travel is animated inside this whole cycle; no extra donor-carousel wait is added.
			AutoloaderCycle = 6f,
			TypeChangeCycle = 8f,
			// «canon de 120 mm modèle F1» / CN120-26, 52 calibres (French Wikipedia, CN120-26)
			GunName = "120mm Gun CN120-26",
			CodexName = "LeclercSXXI CN120-26",
			CodexCaliberMm = 120f,   // GHPC scales the firing shake by it (the T-80B's codex says 125)
			// My choice: the stabiliser and spread of GHPC's Leopard 2A3 / M1IP. No source gives a figure the game can use;
			// the two-plane stabilised gun with the HL 130 slaved to it (Army Guide, Leclerc), LOS stabilised to 50 µrad
			// (French Wikipedia, Leclerc). The T-80B's gun holds 0.05 and spreads 0.011 deg.
			TurretInertia = 0.95f,
			GunInertia = 0.95f,
			Deviation = 0.005f,
			// No CN120-26 recording in GHPC: the Leopard 2's 120 mm (NATO port, NATO parts), the game's own event.
			ShotEvent = "event:/Weapons/canon_120mm-RH120",
		};

		// «le recul est limité à 400 mm pour être compatible avec le chargement automatique» (French Wikipedia, CN120-26):
		// the barrel on the T-80B's recoil bone, its 0.25 m stroke made 0.40 m.
		static readonly GunSetup.Recoil RecoilSettings = new GunSetup.Recoil
		{
			Tag = LeclercSXXIMod.FriendlyName,
			Bone = "gun_recoil",
			Script = "Gun Animation",
			Barrel = "LeclercSXXI_GUN/GUN_BARREL",
			Travel = 0.40f,
		};

		// After PortAxes moved the pivots.
		internal static void Recoil(Vehicle vehicle)
		{
			GunSetup.BarrelRecoil(vehicle, RecoilSettings);
		}

		// At spawn, after the rounds (Firepower.Apply), before the feeds start.
		internal static void Apply(Vehicle vehicle)
		{
			ConveyorLoader.Configure(vehicle);
			GunSetup.Apply(vehicle, Settings);
			ReloadDip.Apply(vehicle);
			AutoloaderSound.Apply(vehicle);
		}
	}
}
