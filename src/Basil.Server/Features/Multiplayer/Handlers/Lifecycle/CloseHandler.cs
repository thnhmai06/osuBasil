namespace Basil.Server.Features.Multiplayer.Handlers.Lifecycle;

/// <summary>Closes a match, parting every seated userSession and tearing the room down.</summary>
public sealed class CloseHandler(MatchLifecycle matchLifecycle)
{
	/// <summary>Closes a match, parting every seated userSession and tearing the room down.</summary>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to close.</param>
	/// <param name="cancellationToken">A token that cancels the close event writes.</param>
	public async Task CloseAsync(int? actorId, string? actorName, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		await matchLifecycle.CloseAsync(match, actorId, actorName, cancellationToken);
	}
}
