namespace Basil.Application.Contracts.Repositories;

/// <summary>Allocates identifiers for new instances of <typeparamref name="T" />.</summary>
/// <typeparam name="T">The kind of object the identifiers are for.</typeparam>
/// <remarks>
///     Every returned id is positive and never returned again for the same <typeparamref name="T" />.
///     For persistent data (such as matches and rounds) uniqueness holds across restarts; for runtime-only
///     data (such as rooms) it holds for as long as the process runs.
/// </remarks>
public interface IIdAllocator<T>
{
	/// <summary>Allocates a new, unused identifier.</summary>
	/// <param name="cancellationToken">A token that cancels the allocation.</param>
	/// <returns>The allocated identifier.</returns>
	ValueTask<int> NextAsync(CancellationToken cancellationToken = default);
}