using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Servers;
using Basil.Bot.Application.Commands;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Application.Chat;

/// <summary>Runs chat commands posted in channels on the connection that typed them.</summary>
internal sealed class ChatHandler(
	Server server,
	ChatCommands commands,
	CommandWorker worker,
	ILogger<ChatHandler> logger)
	: IEventHandler<ChannelEvent>
{
	private const int MaxMessageLength = 2000;

	/// <inheritdoc />
	public ValueTask HandleAsync(ChannelEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not ChannelMessagePosted posted
		    || posted.By is not { } by
		    || posted.Message.IsNotice
		    || string.IsNullOrWhiteSpace(posted.Message.Content)
		    || BasilBot.Is(posted.Message.Author))
			return ValueTask.CompletedTask;

		var privateMessage = posted.Channel is PmChannel pm && BasilBot.Is(pm.Owner.Record);
		if (posted.Channel is PmChannel && !privateMessage) return ValueTask.CompletedTask;

		var context = new CommandContext(
			by,
			privateMessage ? null : posted.Channel.Name,
			(text, ct) => SendSplitAsync(text, (piece, token) => privateMessage
				? server.WhisperAsync(by.User, piece, ct: token)
				: server.SayAsync(posted.Channel, piece, ct: token), ct),
			(text, ct) =>
				SendSplitAsync(text, (piece, token) => server.WhisperAsync(by.User, piece, ct: token), ct));

		worker.Enqueue(by.Record.Id, async ct =>
		{
			try
			{
				await commands.ExecuteAsync(context, posted.Message.Content, ct);
			}
			catch (Exception exception) when
				(exception is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				logger.LogError(exception, "Error handling bot command from user {UserId}", by.Record.Id);
			}
		});
		return ValueTask.CompletedTask;
	}

	private static async Task SendSplitAsync(
		string text,
		Func<string, CancellationToken, Task<bool>> send,
		CancellationToken cancellationToken)
	{
		foreach (var piece in SplitForSend(text))
			await send(piece, cancellationToken);
	}

	private static IEnumerable<string> SplitForSend(string text)
	{
		var lines = text.Split('\n');
		foreach (var rawLine in lines)
		{
			var line = rawLine.TrimEnd('\r');
			if (string.IsNullOrEmpty(line))
				continue;

			if (line.Length <= MaxMessageLength)
			{
				yield return line;
				continue;
			}

			// Split long lines at the last space before the limit
			var remaining = line;
			while (remaining.Length > MaxMessageLength)
			{
				var splitAt = remaining.LastIndexOf(' ', MaxMessageLength);
				if (splitAt <= 0)
				{
					// No space found, hard split
					splitAt = MaxMessageLength;
					yield return remaining[..splitAt];
					remaining = remaining[splitAt..];
				}
				else
				{
					yield return remaining[..splitAt];
					remaining = remaining[(splitAt + 1)..];
				}
			}

			if (remaining.Length > 0)
				yield return remaining;
		}
	}
}