using Basil.Application.Bot.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot.Tests.Support;

/// <summary>
///     Delivers the room and lobby events of a test application to the handlers that cancel countdowns, as the host
///     does for every handler.
/// </summary>
internal static class CountdownEvents
{
	/// <summary>Starts delivering events to the countdown handlers of the harness's application.</summary>
	/// <param name="bot">The harness whose events are delivered.</param>
	/// <returns>The countdowns of the application.</returns>
	public static RoomCountdowns Deliver(BotHarness bot)
	{
		var countdowns = bot.App.Provider.GetRequiredService<RoomCountdowns>();
		var token = TestContext.Current.CancellationToken;
		bot.App.RoomEvents.Recorded += @event =>
		{
			switch (@event)
			{
				case RoomRoundStarted started:
					new CancelCountdownOnRoundStarted(countdowns).Handle(started, token).AsTask().Wait(token);
					break;
				case RoomRoundAborted aborted:
					new CancelCountdownOnRoundAborted(countdowns).Handle(aborted, token).AsTask().Wait(token);
					break;
				case RoomSettingsChanged changed:
					new CancelCountdownOnSettingsChanged(countdowns).Handle(changed, token).AsTask().Wait(token);
					break;
			}
		};
		bot.App.LobbyEvents.Recorded += @event =>
		{
			if (@event is LobbyRoomClosed closed)
				new CancelCountdownOnRoomClosed(countdowns).Handle(closed, token).AsTask().Wait(token);
		};
		return countdowns;
	}
}