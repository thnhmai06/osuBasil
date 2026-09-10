using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Chat;

/// <summary>
///     Removes a departing session from every channel it had joined, applying the shared PART/QUIT
///     rules through <see cref="ChannelMembershipService.DisconnectFromChannels" />.
/// </summary>
public sealed class ChannelPartLogoutHandler(ChannelMembershipService channelMembership) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 30;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		channelMembership.DisconnectFromChannels(session, session is GameSession ? "Logged out" : "Connection closed");
		return Task.CompletedTask;
	}
}
