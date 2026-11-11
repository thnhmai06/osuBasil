using Basil.Application.Bot.Commands;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Servers.Messages.Channels;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers.Commands;

/// <summary>
///     Executes the <c>!channel</c> chat command: <c>list</c>, <c>create</c>, <c>set</c> and <c>delete</c>, one for one
///     with the HTTP API that manages the general channels.
/// </summary>
/// <remarks>
///     Operations run on the server and the channel scoped to the connection of the user who typed the command; the
///     scope decides whether the typist may manage channels, and the command only turns the result into a reply.
/// </remarks>
internal sealed class ChannelCommands(ISender sender, GeneralChannelRegistry channels)
{
	/// <summary>Runs a <c>!channel &lt;subcommand&gt; [args...]</c> command for the sender.</summary>
	/// <param name="context">Who sent the command, where it was sent, and how to answer it.</param>
	/// <param name="args">The tokens after <c>!channel</c>; <paramref name="args" />[0] is the subcommand.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the command succeeded; otherwise <see langword="false" />.</returns>
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
				await context.Reply(ChannelReplies.Usage, ct);
				return false;
		}
	}

	private async Task<bool> ListAsync(CommandContext context, CancellationToken ct)
	{
		var lines = channels.VisibleTo(context.Actor).Select(channel => context.Replies.Text(ChannelReplies.ListEntry,
			new
			{
				name = channel.Name,
				read = (byte)channel.Record.Value.ReadPrivileges,
				write = (byte)channel.Record.Value.WritePrivileges,
				autoJoin = channel.Record.Value.AutoJoin,
				visible = channel.Record.Value.Visible,
				topic = channel.Topic
			})).ToList();
		if (lines.Count == 0) await context.Reply(ChannelReplies.NoChannels, ct);
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
		return result.IsOk;
	}

	private async Task<bool> SetAsync(CommandContext context, string name, IEnumerable<string> options,
		CancellationToken ct)
	{
		if (!TryParse(options, out var change)) return await InvalidAsync(context, ct);

		if (!change.IsValid) return await InvalidAsync(context, ct);

		var (result, channel) = await sender.Send(new ConfigureGeneralChannel(context.Actor, name, change), ct);
		var (reply, arguments) = ChannelReplies.Of(result, channel?.Name ?? name, ChannelReplies.Updated);
		await context.Reply(reply, arguments, ct);
		return result.IsOk;
	}

	private async Task<bool> DeleteAsync(CommandContext context, string name, CancellationToken ct)
	{
		var (result, channel) = await sender.Send(new DeleteGeneralChannel(context.Actor, name), ct);
		var (reply, arguments) = ChannelReplies.Of(result, channel?.Name ?? name, ChannelReplies.Deleted);
		await context.Reply(reply, arguments, ct);
		return result.IsOk;
	}

	private static async Task<bool> InvalidAsync(CommandContext context, CancellationToken ct)
	{
		await context.Reply(ChannelReplies.InvalidArguments, ct);
		return false;
	}

	/// <summary>Reads <c>key=value</c> options: <c>topic</c>, <c>read</c>, <c>write</c>, <c>autojoin</c>, <c>visible</c>.</summary>
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