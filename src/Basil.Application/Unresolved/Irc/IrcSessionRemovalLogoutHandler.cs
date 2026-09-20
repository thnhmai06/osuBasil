using Basil.Application.Registry;
using Basil.Application.Unresolved.Sessions;

namespace Basil.Application.Unresolved.Irc;

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