using Basil.Application.Bot.Replies;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Bot.Commands;

internal sealed record CommandContext(
	Connection Actor,
	string? Channel,
	IReplies Replies,
	Func<string, CancellationToken, Task> Say,
	Func<string, CancellationToken, Task> SayPrivately)
{
	public UserRecord Sender => Actor.Record;

	public Task Reply(Reply reply, CancellationToken ct)
	{
		return Say(Replies.Text(reply), ct);
	}

	public Task Reply(Reply reply, object arguments, CancellationToken ct)
	{
		return Say(Replies.Text(reply, arguments), ct);
	}

	public Task ReplyPrivately(Reply reply, CancellationToken ct)
	{
		return SayPrivately(Replies.Text(reply), ct);
	}

	public Task ReplyPrivately(Reply reply, object arguments, CancellationToken ct)
	{
		return SayPrivately(Replies.Text(reply, arguments), ct);
	}
}