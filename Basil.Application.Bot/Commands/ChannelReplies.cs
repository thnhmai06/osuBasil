using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Objects;

namespace Basil.Application.Bot.Commands;

internal static class ChannelReplies
{
	public static (BotReply Reply, object Arguments) Of(ServerActionResult result, string name)
	{
		return result switch
		{
			ServerActionResult.Denied => (BotReply.ChannelMissingPrivileges, new { }),
			ServerActionResult.Ok => (BotReply.ChannelCreated, new { name }),
			ServerActionResult.NameTaken => (BotReply.ChannelNameTaken, new { name }),
			_ => (BotReply.ChannelRefused, new { outcome = result.ToString() })
		};
	}

	public static (BotReply Reply, object Arguments) Of(ChannelJoinResult result, string name, BotReply success)
	{
		return result switch
		{
			ChannelJoinResult.Denied => (BotReply.ChannelMissingPrivileges, new { }),
			ChannelJoinResult.Ok => (success, new { name }),
			ChannelJoinResult.NotFound or ChannelJoinResult.Closed => (BotReply.ChannelNotFound, new { name }),
			_ => (BotReply.ChannelRefused, new { outcome = result.ToString() })
		};
	}
}