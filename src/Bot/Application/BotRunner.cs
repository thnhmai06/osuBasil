using Basil.Bot.Application.Announcements;
using Basil.Bot.Application.Basil;
using Basil.Bot.Application.Replies;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Application;

/// <summary>Runs BasilBot: reacts to every server event until the host stops.</summary>
internal sealed class BotRunner(
	IBasilEvents events,
	ReplyWriter replyWriter,
	RoomAnnouncer roomAnnouncer,
	AnticheatAnnouncer anticheatAnnouncer,
	ILogger<BotRunner> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		await foreach (var botEvent in events.ReadAllAsync(cancellationToken))
		{
			try
			{
				await HandleEventAsync(botEvent, cancellationToken);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error handling bot event {EventType}", botEvent.GetType().Name);
			}
		}
	}

	private async Task HandleEventAsync(BotEvent botEvent, CancellationToken cancellationToken)
	{
		switch (botEvent)
		{
			case MessageReceived messageReceived:
				await replyWriter.HandleAsync(messageReceived, cancellationToken);
				break;

			case PlayerFlagged:
				await anticheatAnnouncer.HandleAsync(botEvent, cancellationToken);
				break;

			case RoomCountdownStarted:
			case RoomCountdownTicked:
			case RoomCountdownCancelled:
			case RoomCountdownElapsed:
			case RoomRoundStarted:
			case RoomRoundAborted:
			case RoomSettingsChanged:
			case RoomClosingAnnounced:
			case RoomPlayerJoined:
			case RoomClosed:
				await roomAnnouncer.HandleAsync(botEvent, cancellationToken);
				break;
		}
	}
}
