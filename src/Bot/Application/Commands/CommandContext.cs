using Basil.Application.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Commands;

/// <summary>Who typed a command, where it was typed, and how to answer it.</summary>
/// <param name="By">The connection of the user who typed the command; commands run on objects scoped to it.</param>
/// <param name="Channel">The channel it was typed in, or <see langword="null" /> when it was a private message to the bot.</param>
/// <param name="Reply">Answers where the command came from (the channel, or the sender's private messages).</param>
/// <param name="ReplyPrivately">Answers in the sender's private messages wherever the command came from.</param>
internal sealed record CommandContext(
	Connection By,
	string? Channel,
	Func<string, CancellationToken, Task> Reply,
	Func<string, CancellationToken, Task> ReplyPrivately)
{
	/// <summary>Gets the user who typed the command.</summary>
	public UserRecord Sender => By.Record;
}
