namespace LeclercSXXI.Common
{
	internal static class ApsMath
	{
		internal sealed class Block
		{
			public string Name;
			public int Side;
			public int Rounds = 2;
		}

		internal sealed class Geometry
		{
			public Block[] Blocks = new Block[0];
		}

		internal sealed class State
		{
			public int[] Rounds;
			public bool[] Lost;
			public State(Geometry g)
			{
				Rounds = new int[g.Blocks.Length]; Lost = new bool[g.Blocks.Length];
				for (int i = 0; i < Rounds.Length; i++) Rounds[i] = g.Blocks[i].Rounds;
				Drawn = new int[g.Blocks.Length, g.Blocks.Length];
			}

			public int[,] Drawn;
			public void Reload(int block)
			{
				Rounds[block]++;
				int s = Drawn[block, block] > 0 ? block : -1;
				for (int i = 0; i < Rounds.Length && s < 0; i++) if (Drawn[i, block] > 0) s = i;
				if (s >= 0) Drawn[s, block]--;
			}

			public int SideRounds(Geometry g, int side) { int n = 0; for (int i = 0; i < Rounds.Length; i++) if (g.Blocks[i].Side == side && !Lost[i]) n += Rounds[i]; return n; }

			public int Total { get { int n = 0; for (int i = 0; i < Rounds.Length; i++) if (!Lost[i]) n += Rounds[i]; return n; } }
		}
	}
}
