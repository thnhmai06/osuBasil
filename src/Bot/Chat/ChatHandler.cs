using Basil.Application.Chat.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Connections;
using Basil.Bot.Commands;
using Basil.Bot.Replies;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Chat;

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
	public ValueTask Handle(ChannelMessagePosted posted, CancellationToken cancellationToken)
	{
		if (posted.By is not { } by || IsIgnored(posted.Message))
			return ValueTask.CompletedTask;

		var privateMessage = posted.Channel is PmChannel pm && pm.Owner.Record.IsBasilBot();
		if (posted.Channel is PmChannel && !privateMessage) return ValueTask.CompletedTask;

		var context = NewContext(posted, by, privateMessage);
		worker.Enqueue(by.Record.Id, workerToken => RunCommandAsync(context, posted.Message.Content, workerToken));
		return ValueTask.CompletedTask;
	}

	private static bool IsIgnored(Message message)
	{
		return message.IsNotice || message.Author.IsBasilBot();
	}

	private CommandContext NewContext(ChannelMessagePosted posted, Connection by, bool privateMessage)
	{
		return new CommandContext(
			by,
			privateMessage ? null : posted.Channel.Name,
			replies,
			(text, sendToken) => SendSplitAsync(text, (piece, token) => privateMessage
				? server.WhisperAsync(by.User, piece, cancellationToken: token)
				: server.SayAsync(posted.Channel, piece, cancellationToken: token), sendToken),
			(text, sendToken) =>
				SendSplitAsync(text, (piece, token) => server.WhisperAsync(by.User, piece, cancellationToken: token),
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
			logger.LogError(exception, "Error handling bot command from user {UserId}", context.By.Record.Id);
		}
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