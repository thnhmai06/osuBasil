using Basil.Application.Chat.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Bot.Commands;

/// <summary>
///     Executes the <c>!channel</c> chat command: <c>list</c>, <c>create</c>, <c>set</c> and <c>delete</c>, one for one
///     with the HTTP API that manages the general channels.
/// </summary>
/// <remarks>
///     Operations run on the server and the channel scoped to the connection of the user who typed the command; the
///     scope decides whether the typist may manage channels, and the command only turns the result into a reply.
/// </remarks>
internal sealed class ChannelCommands(Server server, GeneralChannelRegistry channels)
{
	/// <summary>Runs a <c>!channel &lt;subcommand&gt; [args...]</c> command for the sender.</summary>
	/// <param name="context">Who sent the command, where it was sent, and how to answer it.</param>
	/// <param name="args">The tokens after <c>!channel</c>; <paramref name="args" />[0] is the subcommand.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the command succeeded; otherwise <see langword="false" />.</returns>
	public async Task<bool> ExecuteAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		switch (args.Count > 0 ? args[0].ToLowerInvariant() : string.Empty)
		{
			case "list" when args.Count == 1:
				return await ListAsync(context, cancellationToken);
			case "create" when args.Count >= 2:
				return await CreateAsync(context, args[1], args.Skip(2), cancellationToken);
			case "set" when args.Count >= 3:
				return await SetAsync(context, args[1], args.Skip(2), cancellationToken);
			case "delete" when args.Count == 2:
				return await DeleteAsync(context, args[1], cancellationToken);
			default:
				await context.Reply(ChannelReplies.Usage, cancellationToken);
				return false;
		}
	}

	private async Task<bool> ListAsync(CommandContext context, CancellationToken cancellationToken)
	{
		var lines = channels.VisibleTo(context.By).Select(channel => context.Replies.Text(ChannelReplies.ListEntry, new
		{
			name = channel.Name,
			read = (byte)channel.Record.Value.ReadPrivileges,
			write = (byte)channel.Record.Value.WritePrivileges,
			autoJoin = channel.Record.Value.AutoJoin,
			visible = channel.Record.Value.Visible,
			topic = channel.Topic
		})).ToList();
		if (lines.Count == 0) await context.Reply(ChannelReplies.NoChannels, cancellationToken);
		else await context.Say(string.Join('\n', lines), cancellationToken);
		return true;
	}

	private async Task<bool> CreateAsync(CommandContext context, string name, IEnumerable<string> options,
		CancellationToken cancellationToken)
	{
		GeneralChannelRecord record;
		try
		{
			if (!TryParse(options, out var change)) return await InvalidAsync(context, cancellationToken);
			record = new GeneralChannelRecord
			{
				Name = name,
				Value = new GeneralChannelData
				{
					Topic = change.Topic ?? string.Empty,
					ReadPrivileges = change.ReadPrivileges ?? UserPrivileges.None,
					WritePrivileges = change.WritePrivileges ?? UserPrivileges.None,
					AutoJoin = change.AutoJoin ?? false,
					Visible = change.Visible ?? true
				}
			};
		}
		catch (ArgumentException)
		{
			return await InvalidAsync(context, cancellationToken);
		}

		var result = await server.ScopeTo(context.By).Channels.CreateAsync(record, cancellationToken);
		var (reply, arguments) = ChannelReplies.Of(result, name);
		await context.Reply(reply, arguments, cancellationToken);
		return result.IsOk;
	}

	private async Task<bool> SetAsync(CommandContext context, string name, IEnumerable<string> options,
		CancellationToken cancellationToken)
	{
		if (!TryParse(options, out var change)) return await InvalidAsync(context, cancellationToken);
		if (channels.Find(name) is not { } channel)
		{
			await context.Reply(ChannelReplies.NotFound, new { name }, cancellationToken);
			return false;
		}

		Application.Common.Result<ChannelOutcome> result;
		try
		{
			result = await channel.ScopeTo(context.By).General!.ConfigureAsync(change, cancellationToken);
		}
		catch (ArgumentException)
		{
			return await InvalidAsync(context, cancellationToken);
		}

		var (reply, arguments) = ChannelReplies.Of(result, channel.Name, ChannelReplies.Updated);
		await context.Reply(reply, arguments, cancellationToken);
		return result.IsOk;
	}

	private async Task<bool> DeleteAsync(CommandContext context, string name, CancellationToken cancellationToken)
	{
		if (channels.Find(name) is not { } channel)
		{
			await context.Reply(ChannelReplies.NotFound, new { name }, cancellationToken);
			return false;
		}

		var result = await channel.ScopeTo(context.By).General!.DeleteAsync(cancellationToken);
		var (reply, arguments) = ChannelReplies.Of(result, channel.Name, ChannelReplies.Deleted);
		await context.Reply(reply, arguments, cancellationToken);
		return result.IsOk;
	}

	private static async Task<bool> InvalidAsync(CommandContext context, CancellationToken cancellationToken)
	{
		await context.Reply(ChannelReplies.InvalidArguments, cancellationToken);
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
