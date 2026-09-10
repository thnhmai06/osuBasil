using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Irc;

/// <summary>
///     Removes a departing <see cref="IrcSession" /> from the live session registry.
/// </summary>
public sealed class IrcSessionRemovalLogoutHandler(ISessionRegistry<IrcSession> ircRegistry) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 40;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is IrcSession irc) ircRegistry.Remove(irc);

		return Task.CompletedTask;
	}
}
