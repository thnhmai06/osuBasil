using System.Runtime;
using Basil.Application.Diagnostics.Capabilities;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Users.Objects.Sessions;

namespace Basil.Application.Diagnostics.Objects;

public sealed class ServerDiagnostics(TimeProvider clock, ICacheCleaner caches, IClearTally exceptions)
{
	public ScopedDiagnostics ScopeTo(Session actor)
	{
		return new ScopedDiagnostics(this, actor.EffectivePrivileges(clock.GetUtcNow()));
	}

	internal int ClearCaches()
	{
		return caches.Clear();
	}

	internal void ResetExceptions()
	{
		exceptions.Reset();
	}

	internal static void CollectGarbage()
	{
		GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
	}
}