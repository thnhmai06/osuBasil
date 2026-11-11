using System.Runtime;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Diagnostics.Capabilities;
using Basil.Application.Users.Objects.Connections;

namespace Basil.Application.Diagnostics.Objects;

/// <summary>The maintenance actions of the running server, which only a connection with the privilege may run.</summary>
/// <param name="clock">The clock of the server.</param>
/// <param name="caches">Empties the cache of derived files.</param>
/// <param name="exceptions">The count of exceptions, which an operator can reset.</param>
public sealed class ServerDiagnostics(TimeProvider clock, ICacheCleaner caches, IClearTally exceptions)
{
	/// <summary>Scopes the diagnostics to the connection that asks for them.</summary>
	/// <param name="actor">The connection acting.</param>
	/// <returns>The diagnostics operations available to that connection.</returns>
	public ScopedDiagnostics ScopeTo(Connection actor)
	{
		return new ScopedDiagnostics(this, actor.EffectivePrivileges(clock.GetUtcNow()));
	}

	/// <summary>Deletes the cached files.</summary>
	/// <returns>How many files were deleted.</returns>
	internal int ClearCaches()
	{
		return caches.Clear();
	}

	/// <summary>Forgets the counted exceptions.</summary>
	internal void ResetExceptions()
	{
		exceptions.Reset();
	}

	/// <summary>Collects the garbage now, compacting the large object heap.</summary>
	internal static void CollectGarbage()
	{
		GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
	}
}