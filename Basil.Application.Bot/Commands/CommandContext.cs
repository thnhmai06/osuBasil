using Basil.Application.Shared.Replies;
using Basil.Application.Bot.Replies;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Bot.Commands;

internal sealed record CommandContext(
	Session Actor,
	string? Channel,
	IReplies<BotReply> Replies,
	Func<string, CancellationToken, Task> Say,
	Func<string, CancellationToken, Task> SayPrivately)
{
	public UserRecord Sender => Actor.Record;

	/// <summary>Gets whether a reply to an unknown command may be said here: in a private message or in a room channel, never in a public channel.</summary>
	public bool AnswersUnknownCommands => Channel is null || RoomChannelRules.TryParse(Channel, out _);

	public Task Reply(BotReply reply, CancellationToken ct)
	{
		return Say(Replies.Text(reply), ct);
	}

	public Task Reply(BotReply reply, object arguments, CancellationToken ct)
	{
		return Say(Replies.Text(reply, arguments), ct);
	}

	public Task ReplyToUnknownCommand(BotReply reply, object arguments, CancellationToken ct)
	{
		return AnswersUnknownCommands ? Say(Replies.Text(reply, arguments), ct) : Task.CompletedTask;
	}

	public Task ReplyPrivately(BotReply reply, CancellationToken ct)
	{
		return SayPrivately(Replies.Text(reply), ct);
	}

	public Task ReplyPrivately(BotReply reply, object arguments, CancellationToken ct)
	{
		return SayPrivately(Replies.Text(reply, arguments), ct);
	}
}