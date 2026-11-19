using Basil.Application.Bot.Replies;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;

namespace Basil.Application.Bot.Commands;

internal static class ChannelReplies
{
	public static readonly Reply Usage = new("channel.usage",
		"Usage: !channel list | create <#name> [topic=<text>] [read=<mask>] [write=<mask>] [autojoin=<true|false>] [visible=<true|false>] | set <#name> <option>=<value>... | delete <#name>");

	public static readonly Reply ListEntry = new("channel.listEntry",
		"{name} read={read} write={write} autojoin={autoJoin} visible={visible} topic={topic}",
		"name", "read", "write", "autoJoin", "visible", "topic");

	public static readonly Reply NoChannels = new("channel.noChannels", "There are no channels you can see.");

	public static readonly Reply Created = new("channel.created", "Created channel {name}.", "name");

	public static readonly Reply Updated = new("channel.updated", "Updated channel {name}.", "name");

	public static readonly Reply Deleted = new("channel.deleted", "Deleted channel {name}.", "name");

	public static readonly Reply NameTaken = new("channel.nameTaken", "A channel named {name} already exists.", "name");

	public static readonly Reply NotFound = new("channel.notFound", "There is no channel named {name}.", "name");

	public static readonly Reply InvalidArguments = new("channel.invalidArguments",
		"That is not a valid channel name or option. Type !channel for the usage.");

	public static readonly Reply MissingPrivileges = new("channel.missingPrivileges",
		"You do not have the privileges needed for that.");

	public static readonly Reply Refused = new("channel.refused", "The server refused that: {outcome}.", "outcome");

	public static (Reply Reply, object Arguments) Of(ServerOutcome result, string name)
	{
		return result switch
		{
			ServerOutcome.Denied => (MissingPrivileges, new { }),
			ServerOutcome.Ok => (Created, new { name }),
			ServerOutcome.NameTaken => (NameTaken, new { name }),
			_ => (Refused, new { outcome = result.ToString() })
		};
	}

	public static (Reply Reply, object Arguments) Of(ChannelOutcome result, string name, Reply success)
	{
		return result switch
		{
			ChannelOutcome.Denied => (MissingPrivileges, new { }),
			ChannelOutcome.Ok => (success, new { name }),
			ChannelOutcome.NotFound or ChannelOutcome.Closed => (NotFound, new { name }),
			_ => (Refused, new { outcome = result.ToString() })
		};
	}
}