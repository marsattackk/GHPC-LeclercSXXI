namespace LeclercSXXI
{
	// What the player reads in MelonPreferences.cfg: one description per setting. Static fields, not consts:
	// tools\check-settings.ps1 reads every const as a possible default.
	// AfterChange stays on top: static fields start in the order they are written, so a text that ends in it needs it set first.
	internal static class SettingText
	{
		static readonly string AfterChange = " For Leclercs spawned after the change.";

		internal static readonly string Enabled =
			"Adds the Leclerc SXXI as its own unit (Instant Action, campaign roster, ATAC key \"LeclercSXXI\").";

		internal static readonly string Apfsds =
			"Default APFSDS where the game does not choose one (the mission customizer's ammo dropdown and the campaign's ammo"
			+ " supply override it): OFLF2 = OFL 120 F2 (default; the heavier dart of the SXXI's day, about 800 mm) or OFLF1 ="
			+ " OFL 120 F1 (the first Leclerc round, faster, about 700 mm).";

		internal static readonly string ForceApfsds =
			"false (default): the campaign picks each Leclerc's APFSDS from the army's ammo supply (OFL 120 F2 or OFL 120 F1, even odds)."
			+ " true = every Leclerc carries the APFSDS setting's round in the campaign too. In Instant Action the mission customizer's"
			+ " ammo dropdown still decides."
			+ AfterChange;
	}
}
