using System;

namespace LeclercSXXI.Common
{
	internal static class GunShotRules
	{
		internal static float ChannelVolume(float volume, float listenerVolume) => Math.Max(0f, volume) * Math.Max(0f, Math.Min(1f, listenerVolume));

		internal static bool IsPcmWav(byte[] b, out uint lengthMs)
		{
			lengthMs = 0;
			if (b == null || b.Length < 44 || Tag(b, 0) != "RIFF" || Tag(b, 8) != "WAVE") return false;
			int p = 12, rate = 0, channels = 0, bits = 0, format = 0;
			while (p + 8 <= b.Length)
			{
				string id = Tag(b, p);
				uint size = BitConverter.ToUInt32(b, p + 4);
				if (id == "fmt " && size >= 16 && p + 24 <= b.Length)
				{
					format = BitConverter.ToUInt16(b, p + 8);
					channels = BitConverter.ToUInt16(b, p + 10);
					rate = BitConverter.ToInt32(b, p + 12);
					bits = BitConverter.ToUInt16(b, p + 22);
				}
				else if (id == "data")
				{
					if (format != 1 || channels < 1 || rate < 1 || bits < 8) return false;
					long bytes = Math.Min(size, b.Length - p - 8);
					if (bytes == 0) return false;
					lengthMs = (uint)(bytes * 1000 / (rate * channels * (bits / 8)));
					return true;
				}
				long next = p + 8L + size + (size & 1);
				if (next > b.Length) return false;
				p = (int)next;
			}
			return false;
		}

		static string Tag(byte[] b, int at) => new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] });
	}
}
