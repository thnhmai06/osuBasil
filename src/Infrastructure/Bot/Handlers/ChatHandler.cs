using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Bot.Handlers.Commands;
using Basil.Infrastructure.Bot.Workers;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Runs chat commands posted in channels on the connection that typed them.</summary>
internal sealed class ChatHandler(
	Server server,
	ChatCommands commands,
	CommandWorker worker,
	IReplies replies,
	ILogger<ChatHandler> logger)
	: INotificationHandler<ChannelMessagePosted>
{
	private const int MaxMessageLength = 2000;

	/// <inheritdoc />
	public ValueTask Handle(ChannelMessagePosted posted, CancellationToken ct)
	{
		// A message the server said has no actor, so BasilBot's own replies are never read as commands.
		if (posted.Actor is not { } actor || IsIgnored(posted.Message, actor))
			return ValueTask.CompletedTask;

		var privateMessage = posted.Channel is PmChannel pm && pm.Owner.Record.IsBasilBot();
		if (posted.Channel is PmChannel && !privateMessage) return ValueTask.CompletedTask;

		// Ordinary channel chatter is not a command; only the commands reach the worker queue.
		if (!privateMessage && !posted.Message.Content.StartsWith(ChatCommands.CommandPrefix, StringComparison.Ordinal))
			return ValueTask.CompletedTask;

		var context = NewContext(posted, actor, privateMessage);
		worker.Enqueue(actor.Record.Id, workerToken => RunCommandAsync(context, posted.Message.Content, workerToken));
		return ValueTask.CompletedTask;
	}

	// The operator speaks as BasilBot, so its messages are commands typed by the operator; the bot's own connections
	// never are.
	private static bool IsIgnored(Message message, Connection actor)
	{
		return message.IsNotice || (message.Author.IsBasilBot() && actor is not OperatorConnection);
	}

	private CommandContext NewContext(ChannelMessagePosted posted, Connection actor, bool privateMessage)
	{
		return new CommandContext(
			actor,
			privateMessage ? null : posted.Channel.Name,
			replies,
			(text, sendToken) => SendSplitAsync(text, (piece, token) => privateMessage
				? server.WhisperAsync(actor.User, piece, ct: token)
				: server.SayAsync(posted.Channel, piece, ct: token), sendToken),
			(text, sendToken) =>
				SendSplitAsync(text, (piece, token) => server.WhisperAsync(actor.User, piece, ct: token),
					sendToken));
	}

	private async Task RunCommandAsync(CommandContext context, string content, CancellationToken workerToken)
	{
		try
		{
			await commands.ExecuteAsync(context, content, workerToken);
		}
		catch (Exception exception) when
			(exception is not OperationCanceledException || !workerToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Error handling bot command from user {UserId}", context.Actor.Record.Id);
		}
	}

	private static async Task SendSplitAsync(
		string text,
		Func<string, CancellationToken, Task<bool>> send,
		CancellationToken ct)
	{
		foreach (var piece in SplitForSend(text))
			await send(piece, ct);
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