using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Objects;
using Basil.Application.Common;
using Basil.Application.Servers.Objects;

namespace Basil.Application.Bot.Commands;

/// <summary>The replies of the <c>!channel</c> command, which manages the general channels like the HTTP API does.</summary>
internal static class ChannelReplies
{
	/// <summary>Reply when the command is typed without a valid subcommand.</summary>
	public static readonly Reply Usage = new("channel.usage",
		"Usage: !channel list | create <#name> [topic=<text>] [read=<mask>] [write=<mask>] [autojoin=<true|false>] [visible=<true|false>] | set <#name> <option>=<value>... | delete <#name>");

	/// <summary>Reply for each channel of <c>!channel list</c>.</summary>
	public static readonly Reply ListEntry = new("channel.listEntry",
		"{name} read={read} write={write} autojoin={autoJoin} visible={visible} topic={topic}",
		"name", "read", "write", "autoJoin", "visible", "topic");

	/// <summary>Reply when no channel is visible to the typist.</summary>
	public static readonly Reply NoChannels = new("channel.noChannels", "There are no channels you can see.");

	/// <summary>Reply when a channel was created.</summary>
	public static readonly Reply Created = new("channel.created", "Created channel {name}.", "name");

	/// <summary>Reply when a channel was changed.</summary>
	public static readonly Reply Updated = new("channel.updated", "Updated channel {name}.", "name");

	/// <summary>Reply when a channel was deleted.</summary>
	public static readonly Reply Deleted = new("channel.deleted", "Deleted channel {name}.", "name");

	/// <summary>Reply when the name is already used by another channel.</summary>
	public static readonly Reply NameTaken = new("channel.nameTaken", "A channel named {name} already exists.", "name");

	/// <summary>Reply when no open channel has the name.</summary>
	public static readonly Reply NotFound = new("channel.notFound", "There is no channel named {name}.", "name");

	/// <summary>Reply when the name or an option is not valid.</summary>
	public static readonly Reply InvalidArguments = new("channel.invalidArguments",
		"That is not a valid channel name or option. Type !channel for the usage.");

	/// <summary>Reply when the typist lacks privileges the operation needs.</summary>
	public static readonly Reply MissingPrivileges = new("channel.missingPrivileges",
		"You do not have the privileges needed for that.");

	/// <summary>Reply when the server refuses for a reason without a reply of its own; <c>{outcome}</c> names it.</summary>
	public static readonly Reply Refused = new("channel.refused", "The server refused that: {outcome}.", "outcome");

	/// <summary>Maps the result of creating a channel to the reply and its arguments.</summary>
	/// <param name="result">The result returned by the server.</param>
	/// <param name="name">The channel name.</param>
	/// <returns>The reply and the arguments to fill it with.</returns>
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

	/// <summary>Maps the result of changing or deleting a channel to the reply and its arguments.</summary>
	/// <param name="result">The result returned by the channel.</param>
	/// <param name="name">The channel name.</param>
	/// <param name="success">The reply when the operation succeeded.</param>
	/// <returns>The reply and the arguments to fill it with.</returns>
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