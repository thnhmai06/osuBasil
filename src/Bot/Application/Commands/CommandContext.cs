using Basil.Domain.Users;

namespace Basil.Bot.Application.Commands;

/// <summary>Who sent a command, where it was sent, and how to answer it.</summary>
/// <param name="Sender">The user who sent the command.</param>
/// <param name="Channel">The channel it was sent in, or <see langword="null" /> when it was a private message to the bot.</param>
/// <param name="Reply">Answers where the command came from (the channel, or the sender's private messages).</param>
/// <param name="ReplyPrivately">Answers in the sender's private messages wherever the command came from.</param>
internal sealed record CommandContext(
	User Sender,
	string? Channel,
	Func<string, CancellationToken, Task> Reply,
	Func<string, CancellationToken, Task> ReplyPrivately);
