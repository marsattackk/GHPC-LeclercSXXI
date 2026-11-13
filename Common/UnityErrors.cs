using System;
using System.Runtime.CompilerServices;
using MelonLoader;

namespace LeclercSXXI.Common
{
	// Exceptions in my MonoBehaviours and coroutines only reach Unity's own log, where nobody looks.
	internal static class UnityErrors
	{
		[ThreadStatic] static bool _inside;

		// MelonLoader names the mod from the calling frame.
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Write(string s) => MelonLogger.Error(s);
	}
}
