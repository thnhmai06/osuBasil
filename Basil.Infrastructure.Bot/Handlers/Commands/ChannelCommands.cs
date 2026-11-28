using Basil.Application.Bot.Replies;
using Basil.Application.Bot.Commands;
using Basil.Application.Chat.Registry;
using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Mediator;
using Basil.Application.Chat.Objects;

namespace Basil.Infrastructure.Bot.Handlers.Commands;

internal sealed class ChannelCommands(ISender sender, GeneralChannelRegistry channels)
{
	public async Task<bool> ExecuteAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		switch (args.Count > 0 ? args[0].ToLowerInvariant() : string.Empty)
		{
			case "list" when args.Count == 1:
				return await ListAsync(context, ct);
			case "create" when args.Count >= 2:
				return await CreateAsync(context, args[1], args.Skip(2), ct);
			case "set" when args.Count >= 3:
				return await SetAsync(context, args[1], args.Skip(2), ct);
			case "delete" when args.Count == 2:
				return await DeleteAsync(context, args[1], ct);
			default:
				await context.Reply(BotReply.ChannelUsage, ct);
				return false;
		}
	}

	private static string YesNo(CommandContext context, bool value)
	{
		return context.Replies.Text(value ? BotReply.BotYes : BotReply.BotNo);
	}

	private static string Names(CommandContext context, UserPrivileges privileges)
	{
		return privileges == UserPrivileges.None
			? context.Replies.Text(BotReply.MpNone)
			: privileges.ToString().Replace(", ", "+");
	}

	private async Task<bool> ListAsync(CommandContext context, CancellationToken ct)
	{
		var lines = channels.VisibleTo(context.Actor).Select(channel => context.Replies.Text(BotReply.ChannelListEntry,
			new
			{
				name = channel.Name,
				read = Names(context, channel.Record.Value.ReadPrivileges),
				write = Names(context, channel.Record.Value.WritePrivileges),
				autoJoin = YesNo(context, channel.Record.Value.AutoJoin),
				visible = YesNo(context, channel.Record.Value.Visible),
				topic = channel.Topic
			})).ToList();
		if (lines.Count == 0) await context.Reply(BotReply.ChannelNoChannels, ct);
		else await context.Say(string.Join('\n', lines), ct);
		return true;
	}

	private async Task<bool> CreateAsync(CommandContext context, string name, IEnumerable<string> options,
		CancellationToken ct)
	{
		if (!TryParse(options, out var change)) return await InvalidAsync(context, ct);
		var topic = change.Topic ?? string.Empty;
		var read = change.ReadPrivileges ?? UserPrivileges.None;
		var write = change.WritePrivileges ?? UserPrivileges.None;
		if (!GeneralChannelRecord.IsValidName(name) || !GeneralChannelData.IsValidTopic(topic) ||
		    !GeneralChannelData.IsValidPrivileges(read) || !GeneralChannelData.IsValidPrivileges(write))
			return await InvalidAsync(context, ct);

		var record = new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData
			{
				Topic = topic,
				ReadPrivileges = read,
				WritePrivileges = write,
				AutoJoin = change.AutoJoin ?? false,
				Visible = change.Visible ?? true
			}
		};

		var result = await sender.Send(new CreateGeneralChannel(context.Actor, record), ct);
		var (reply, arguments) = ChannelReplies.Of(result, name);
		await context.Reply(reply, arguments, ct);
		return result == ServerActionResult.Ok;
	}

	private async Task<bool> SetAsync(CommandContext context, string name, IEnumerable<string> options,
		CancellationToken ct)
	{
		if (!TryParse(options, out var change)) return await InvalidAsync(context, ct);

		if (!change.IsValid) return await InvalidAsync(context, ct);

		var (result, channel) = await sender.Send(new ConfigureGeneralChannel(context.Actor, name, change), ct);
		var (reply, arguments) = ChannelReplies.Of(result, channel?.Name ?? name, BotReply.ChannelUpdated);
		await context.Reply(reply, arguments, ct);
		return result == ChannelJoinResult.Ok;
	}

	private async Task<bool> DeleteAsync(CommandContext context, string name, CancellationToken ct)
	{
		var (result, channel) = await sender.Send(new DeleteGeneralChannel(context.Actor, name), ct);
		var (reply, arguments) = ChannelReplies.Of(result, channel?.Name ?? name, BotReply.ChannelDeleted);
		await context.Reply(reply, arguments, ct);
		return result == ChannelJoinResult.Ok;
	}

	private static async Task<bool> InvalidAsync(CommandContext context, CancellationToken ct)
	{
		await context.Reply(BotReply.ChannelInvalidArguments, ct);
		return false;
	}

	private static bool TryParse(IEnumerable<string> options, out GeneralChannelChange change)
	{
		change = new GeneralChannelChange();
		foreach (var option in options)
		{
			var split = option.IndexOf('=');
			if (split <= 0) return false;
			var (key, value) = (option[..split].ToLowerInvariant(), option[(split + 1)..]);
			switch (key)
			{
				case "topic":
					change = change with { Topic = value };
					break;
				case "read" when byte.TryParse(value, out var read):
					change = change with { ReadPrivileges = (UserPrivileges)read };
					break;
				case "write" when byte.TryParse(value, out var write):
					change = change with { WritePrivileges = (UserPrivileges)write };
					break;
				case "autojoin" when bool.TryParse(value, out var autoJoin):
					change = change with { AutoJoin = autoJoin };
					break;
				case "visible" when bool.TryParse(value, out var visible):
					change = change with { Visible = visible };
					break;
				default:
					return false;
			}
		}

		return true;
	}
}