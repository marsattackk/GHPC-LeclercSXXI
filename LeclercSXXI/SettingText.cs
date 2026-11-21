namespace LeclercSXXI
{
	// What the player reads in MelonPreferences.cfg: one description per setting. Static fields, not consts:
	// tools\check-settings.ps1 reads every const as a possible default.
	// AfterChange stays on top: static fields start in the order they are written, so a text that ends in it needs it set first.
	internal static class SettingText
	{
		static readonly string AfterChange = " For Leclercs spawned after the change.";

		internal static readonly string Enabled =
			"Adds the Leclerc SXXI as its own unit (Instant Action, campaign roster). Its unit key is \"LeclercSXXI\", the name"
			+ " to give mods like Any Tank Any Campaign.";

		internal static readonly string Apfsds =
			"Default APFSDS where the game does not choose one (the mission customizer's ammo dropdown and the campaign's ammo"
			+ " supply override it): OFLF2 = OFL 120 F2 (default; the heavier dart of the SXXI's day, about 800 mm) or OFLF1 ="
			+ " OFL 120 F1 (the first Leclerc round, faster, about 700 mm).";

		internal static readonly string ForceApfsds =
			"false (default): the campaign picks each Leclerc's APFSDS from the army's ammo supply (OFL 120 F2 or OFL 120 F1, even odds)."
			+ " true = every Leclerc carries the APFSDS setting's round in the campaign too. In Instant Action the mission customizer's"
			+ " ammo dropdown still decides."
			+ AfterChange;

		internal static readonly string RandomApfsds =
			"false (default): each Leclerc carries the APFSDS setting's round where the game does not choose one."
			+ " true = each Leclerc rolls its own APFSDS from APFSDS pool in Instant Action and scripted missions, and the mission"
			+ " customizer's ammo dropdown starts on Random (a round picked there still decides). The campaign keeps its own ammo supply."
			+ AfterChange;

		internal static readonly string ApfsdsPool =
			"The rounds Random APFSDS rolls from, as name:weight, comma separated (OFLF2, OFLF1; \"OFLF2:3, OFLF1:1\" rolls OFLF2"
			+ " three times as often as OFLF1; a name without a weight counts 1). Empty (default): every APFSDS at its campaign weight."
			+ AfterChange;

		internal static readonly string AmmunitionLoad =
			"How much ammunition each Leclerc carries: Full (default, 40 rounds) or Autoloader (only the 22 in the bustle autoloader)."
			+ " Fewer rounds aboard, fewer to set off when the hull is hit. Applies to every Leclerc, friend and foe."
			+ AfterChange;

		internal static readonly string EmptyRacks =
			"Racks left empty, by name, comma separated (hull: the drum in the hull); when set it replaces Ammunition load."
			+ " The autoloader is always loaded. Empty (default): Ammunition load decides."
			+ AfterChange;
	}
}
