using System.Text.Json;
using Basil.Application.Sessions;
using Basil.Application.Shared.Json;

namespace Basil.Application.Spectating;

/// <summary>
///     Publishes a departing <see cref="GameSession" />'s offline status to its live status stream, so
///     a client watching <c>/users/{id}/live</c> learns the session went offline.
/// </summary>
public sealed class StatusPublishLogoutHandler(IPlayerStatusEvents statusEvents) : IPlayerLogoutHandler
{
	/// <inheritdoc />
	public int Order => 50;

	/// <inheritdoc />
	public Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken)
	{
		if (session is GameSession game && statusEvents.HasSubscribers)
			statusEvents.PublishStatus(game.Id,
				JsonSerializer.SerializeToUtf8Bytes(PlayerStatusView.Build(null), BasilJsonOptions.Instance));

		return Task.CompletedTask;
	}
}