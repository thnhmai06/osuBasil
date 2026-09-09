using Basil.Domain.Multiplayer;
using Basil.Server.Shared.Sessions;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer.Handlers.Slots;

/// <summary>Assigns a single userSession's team.</summary>
public sealed class SetTeamHandler(MatchLifecycle matchLifecycle, ILogger<SetTeamHandler> logger)
{
	public enum TeamResult : byte
	{
		Ok,
		TargetNotInMatch
	}

	/// <summary>Assigns a userSession's team and broadcasts the resulting state.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="target">The userSession whose team to set.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	/// <returns>
	///     <see cref="TeamResult.Ok" /> on success, or <see cref="TeamResult.TargetNotInMatch" /> when
	///     the target occupies no slot in this match.
	/// </returns>
	public Task<TeamResult> SetTeamAsync(MatchSession match, UserSession target, MatchTeam team,
		MatchMutationScope mutation, CancellationToken cancellationToken = default)
	{
		var slot = match.GetSlot(target.Id);
		if (slot is null) return Task.FromResult(TeamResult.TargetNotInMatch);

		slot.Team = team;
		logger.LogDebug("Room settings changed: MatchId={MatchId} UserId={UserId} Team={Team}",
			match.DbId, target.Id, team);
		mutation.PublishState(lobby: false);
		matchLifecycle.CancelQueuedAutoStart(match);
		return Task.FromResult(TeamResult.Ok);
	}
}
