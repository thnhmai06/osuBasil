using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Handlers;
using Basil.Infrastructure.Bot.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot.Tests.Support;

internal static class CountdownEvents
{
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