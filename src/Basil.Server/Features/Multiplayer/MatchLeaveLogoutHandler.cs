using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Removes a departing <see cref="GameSession" /> from its multiplayer match, if it was in one.
/// </summary>
public sealed class MatchLeaveLogoutHandler(MatchMembership matchMembership) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 10;

	/// <inheritdoc />
	public async Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is not GameSession { Match: { } match } game) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		await matchMembership.LeaveAsync(game, match, cancellationToken);
		mutation.PublishState();
	}
}
