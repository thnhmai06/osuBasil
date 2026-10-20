using Basil.Domain.Users;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Bot.Application.Commands;

/// <summary>Who sent a command, where it was sent, and how to answer it.</summary>
/// <param name="Sender">The user who sent the command.</param>
/// <param name="Channel">The channel it was sent in, or <see langword="null" /> when it was a private message to the bot.</param>
/// <param name="Reply">Answers where the command came from (the channel, or the sender's private messages).</param>
/// <param name="ReplyPrivately">Answers in the sender's private messages wherever the command came from.</param>
/// <param name="By">The connection of the user who typed the command; commands run on objects scoped to it.</param>
internal sealed record CommandContext(
	UserRecord Sender,
	string? Channel,
	Func<string, CancellationToken, Task> Reply,
	Func<string, CancellationToken, Task> ReplyPrivately,
	Connection? By = null);