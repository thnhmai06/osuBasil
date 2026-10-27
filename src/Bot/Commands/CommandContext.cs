using Basil.Application.Users.Connections;
using Basil.Bot.Replies;
using Basil.Domain.Users;

namespace Basil.Bot.Commands;

/// <summary>Who typed a command, where it was typed, and how to answer it.</summary>
/// <param name="By">The connection of the user who typed the command; commands run on objects scoped to it.</param>
/// <param name="Channel">The channel it was typed in, or <see langword="null" /> when it was a private message to the bot.</param>
/// <param name="Replies">Gives the text of the replies the command says.</param>
/// <param name="Say">Says a text where the command came from (the channel, or the sender's private messages).</param>
/// <param name="SayPrivately">Says a text in the sender's private messages wherever the command came from.</param>
internal sealed record CommandContext(
	Connection By,
	string? Channel,
	IReplies Replies,
	Func<string, CancellationToken, Task> Say,
	Func<string, CancellationToken, Task> SayPrivately)
{
	/// <summary>Gets the user who typed the command.</summary>
	public UserRecord Sender => By.Record;

	/// <summary>Answers where the command came from.</summary>
	/// <param name="reply">The reply to say.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task Reply(Reply reply, CancellationToken cancellationToken)
	{
		return Say(Replies.Text(reply), cancellationToken);
	}

	/// <summary>Answers where the command came from.</summary>
	/// <param name="reply">The reply to say.</param>
	/// <param name="arguments">The values of the reply's arguments, as an object with a property for each.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task Reply(Reply reply, object arguments, CancellationToken cancellationToken)
	{
		return Say(Replies.Text(reply, arguments), cancellationToken);
	}

	/// <summary>Answers in the sender's private messages.</summary>
	/// <param name="reply">The reply to say.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task ReplyPrivately(Reply reply, CancellationToken cancellationToken)
	{
		return SayPrivately(Replies.Text(reply), cancellationToken);
	}

	/// <summary>Answers in the sender's private messages.</summary>
	/// <param name="reply">The reply to say.</param>
	/// <param name="arguments">The values of the reply's arguments, as an object with a property for each.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task ReplyPrivately(Reply reply, object arguments, CancellationToken cancellationToken)
	{
		return SayPrivately(Replies.Text(reply, arguments), cancellationToken);
	}
}