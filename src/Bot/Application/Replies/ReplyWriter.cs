using Basil.Bot.Application.Basil;
using Basil.Bot.Application.Commands;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Replies;

/// <summary>Writes replies to bot events.</summary>
internal sealed class ReplyWriter(IBasilChat chat, ChatCommands commands)
{
	private const int MaxMessageLength = 2000;

	/// <summary>Handles a received message by building a context and executing commands.</summary>
	public async Task HandleAsync(MessageReceived message, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(message.Message.Content))
			return;

		var context = BuildContext(message.Message, message.Channel);
		await commands.ExecuteAsync(context, message.Message.Content, cancellationToken);
	}

	private CommandContext BuildContext(Message message, string? channel)
	{
		var sender = message.Author;

		if (channel is not null)
		{
			// From a channel: Reply posts in channel, ReplyPrivately sends PM
			return new CommandContext(
				sender,
				channel,
				(text, ct) => SendSplitAsync(text, channel, null, ct),
				(text, ct) => SendSplitAsync(text, null, sender, ct));
		}
		else
		{
			// From a PM: both Reply and ReplyPrivately send PM
			return new CommandContext(
				sender,
				null,
				(text, ct) => SendSplitAsync(text, null, sender, ct),
				(text, ct) => SendSplitAsync(text, null, sender, ct));
		}
	}

	private async Task SendSplitAsync(string text, string? channel, User? user, CancellationToken cancellationToken)
	{
		foreach (var piece in SplitForSend(text))
		{
			if (channel is not null)
				await chat.PostAsync(channel, piece, cancellationToken);
			else if (user is not null)
				await chat.SendAsync(user, piece, cancellationToken);
		}
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
