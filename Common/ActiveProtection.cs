using System.Collections.Generic;
using GHPC;
using GHPC.Crew;
using GHPC.Player;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;
using DC = GHPC.Equipment.DestructibleComponent;

namespace LeclercSXXI.Common
{
	internal static class Aps
	{
		internal sealed class BlockSpec
		{
			public string Module;
		}

		internal sealed class Settings
		{
			public string Name = "APS";
			public bool HatchInterlock = true;
			public BlockSpec[] Blocks = new BlockSpec[0];
			public string[] SensorModules = new string[0];
			public string ComputerModule;
			public float ReloadPerRound = 60f, ReloadMaxSpeed = 0.5f;
		}

		internal sealed class Station : MonoBehaviour
		{
			internal Settings S;
			internal Vehicle Vehicle;
			internal ApsMath.Geometry G;
			internal ApsMath.State St;
			internal bool On { get; private set; }

			internal float ReadyAt;
			internal bool Reloading;
			internal float ReloadLeft;
			internal DC[] BlockDc, SensorDc;
			internal DC ComputerDc;
			internal Renderer[][] Caps;
			bool _modulesBound;
			float _nextModuleLook;

			internal void ShowCaps()
			{
				if (Caps == null) return;
				for (int i = 0; i < Caps.Length; i++)
					for (int k = 0; k < Caps[i].Length; k++)
						if (Caps[i][k] != null) Caps[i][k].enabled = !St.Lost[i] && k < St.Rounds[i];
			}

			void BindModules()
			{
				if (_modulesBound || Time.time < _nextModuleLook) return;
				_nextModuleLook = Time.time + 1f;
				var set = Vehicle.GetComponent<ModuleBuilder.ModuleSet>();
				if (set == null || set.Built.Count == 0) return;
				_modulesBound = true;
				foreach (var (m, dc) in set.Built)
				{
					for (int i = 0; i < S.Blocks.Length; i++) if (S.Blocks[i].Module != null && m.Id == S.Blocks[i].Module) BlockDc[i] = dc;
					for (int k = 0; k < S.SensorModules.Length && k < 2; k++) if (S.SensorModules[k] != null && m.Id == S.SensorModules[k]) SensorDc[k] = dc;
					if (S.ComputerModule != null && m.Id == S.ComputerModule) ComputerDc = dc;
				}
			}

			internal string NotReady()
			{
				if (Vehicle == null || Vehicle.Neutralized) return "unit knocked out";
				if (!On) return "switched off";
				if (ComputerDc != null && ComputerDc.IsDestroyed) return "computer destroyed";
				if (Time.time < ReadyAt) return $"warming up, {ReadyAt - Time.time:0} s left";
				if (Reloading) return "reloading";
				if (S.HatchInterlock && HatchOpen() is string h
				) return $"hatch open ({h})";
				return null;
			}

			internal string HatchOpen()
			{
				var crew = Vehicle != null ? Vehicle.GetComponent<CrewManager>()?.CrewMembers : null;
				if (crew == null) return null;
				foreach (var c in crew) if (c?.ExitHatch != null && c.ExitHatch.Openness > 0.05f) return c.Position.ToString().ToLowerInvariant();
				return null;
			}

			internal bool IsPlayer => PlayerInput.Instance != null && PlayerInput.Instance.CurrentPlayerUnit == Vehicle;

			internal int Empty { get { int n = 0; for (int i = 0; i < St.Rounds.Length; i++) if (!St.Lost[i]) n += G.Blocks[i].Rounds - St.Rounds[i]; return n; } }

			internal void StopReload(string why)
			{
				if (!Reloading) return;
				Reloading = false;
				if (IsPlayer) MelonLogger.Msg($"{S.Name}: reload {why}; {St.Total} rounds loaded.");
			}

			internal bool Stopped
			{
				get { var rb = Vehicle != null ? Vehicle.GetComponent<Rigidbody>() : null; return rb == null || rb.velocity.magnitude <= S.ReloadMaxSpeed; }
			}

			void Update()
			{
				if (Vehicle == null || G == null) return;
				BindModules();
				for (int i = 0; i < BlockDc.Length; i++)
					if (!St.Lost[i] && BlockDc[i] != null && BlockDc[i].IsDestroyed)
					{
						St.Lost[i] = true;
						if (IsPlayer) MelonLogger.Msg($"{S.Name}: {G.Blocks[i].Name} launcher block destroyed.");
						ShowCaps();
					}
				if (Reloading)
				{
					if (Vehicle.Neutralized) { StopReload("stopped: unit knocked out"); return; }
					if (!Stopped) return;
					ReloadLeft -= Time.deltaTime;
					if (ReloadLeft > 0f) return;
					int i;
					for (i = 0; i < St.Rounds.Length; i++) if (!St.Lost[i] && St.Rounds[i] < G.Blocks[i].Rounds) break;
					if (i < St.Rounds.Length) { St.Reload(i); ShowCaps(); }
					if (Empty == 0) StopReload("done");
					else ReloadLeft = S.ReloadPerRound;
				}
			}

			void OnDestroy() => Stations.Remove(this);

			internal string HudLine()
			{
				string why = NotReady();
				string state = why == null ? "ready"
					: Reloading ? (Stopped ? $"reloading, {ReloadLeft:0} s ({Empty} to load)" : $"reloading: stop the tank ({Empty} to load)")
					: why;
				return $"{S.Name}: L {St.SideRounds(G, 0)} R {St.SideRounds(G, 1)}, {state}";
			}
		}

		internal static readonly List<Station> Stations = new List<Station>();

		internal static Station Of(Unit u)
		{
			if (u == null) return null;
			for (int i = 0; i < Stations.Count; i++) { var s = Stations[i]; if (s != null && s.Vehicle == u) return s; }
			return null;
		}

		internal static string HudLine(Unit u) { var s = Of(u); return s != null && s.G != null ? s.HudLine() : null; }

		internal static string HudName(Unit u) => Of(u)?.S.Name;
	}
}
