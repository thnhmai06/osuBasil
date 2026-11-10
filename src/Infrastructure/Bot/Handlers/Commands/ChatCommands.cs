using Basil.Application.Bot.Commands;
using Basil.Application.Chat.Objects;
using Basil.Application.Content.Storage;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;

namespace Basil.Infrastructure.Bot.Handlers.Commands;

/// <summary>Dispatches chat commands to their handlers.</summary>
internal sealed class ChatCommands(
	IUserRepository users,
	IFaqStorage faqs,
	MpCommands mpCommands,
	ChannelCommands channelCommands,
	Random random)
{
	/// <summary>The text that starts a command.</summary>
	public const string CommandPrefix = "!";

	/// <summary>Executes a command if the text starts with the command prefix.</summary>
	/// <param name="context">The command context.</param>
	/// <param name="text">The message text.</param>
	/// <param name="ct">A cancellation token.</param>
	/// <returns>True if a command was executed; otherwise false (no reply sent for unknown commands).</returns>
	public async Task<bool> ExecuteAsync(CommandContext context, string text, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(text))
			return false;

		var isPrivate = context.Channel is null;
		var hasPrefix = text.StartsWith(CommandPrefix, StringComparison.Ordinal);

		if (!hasPrefix && !isPrivate)
			return false;

		// Rebuild with prefix so the splitter sees every segment as a full command
		var commandText = hasPrefix ? text : CommandPrefix + text;
		var segments = CommandLine.Split(commandText);

		if (segments.Count == 1)
			return await ExecuteSingleAsync(context, segments[0].Text, ct);

		if (!await IsChainAllowedAsync(context, segments, ct)) return false;

		return await ExecuteChainAsync(context, segments, ct);
	}

	private async Task<bool> IsChainAllowedAsync(CommandContext context, IReadOnlyList<CommandSegment> segments,
		CancellationToken ct)
	{
		if (context.Channel == GeneralChannel.LobbyName)
		{
			await context.Reply(BotReplies.ChainNotInLobby, ct);
			return false;
		}

		foreach (var segment in segments)
		{
			var stripped = StripPrefix(segment.Text);
			var tokens = CommandLine.Tokenize(stripped);
			if (tokens.Count != 0
			    && string.Equals(tokens[0], "mp", StringComparison.OrdinalIgnoreCase)
			    && tokens.Count >= 2 && MpCommands.IsChainable(tokens[1])) continue;

			await context.Reply(BotReplies.ChainOnlyMpAllowed, ct);
			return false;
		}

		return true;
	}

	private async Task<bool> ExecuteChainAsync(CommandContext context, IReadOnlyList<CommandSegment> segments,
		CancellationToken ct)
	{
		var succeeded = true;
		foreach (var segment in segments)
		{
			if (segment.Operator == ChainOperator.And && !succeeded)
				break;

			var stripped = StripPrefix(segment.Text);
			var tokens = CommandLine.Tokenize(stripped);
			if (tokens.Count == 0)
				continue;

			var args = tokens.Skip(1).ToArray();
			succeeded = await mpCommands.ExecuteAsync(context, args, ct);
		}

		return succeeded;
	}

	private static string StripPrefix(string segmentText)
	{
		return segmentText.StartsWith(CommandPrefix, StringComparison.Ordinal)
			? segmentText[CommandPrefix.Length..]
			: segmentText;
	}

	private async Task<bool> ExecuteSingleAsync(CommandContext context, string segmentText,
		CancellationToken ct)
	{
		var stripped = StripPrefix(segmentText);
		var tokens = CommandLine.Tokenize(stripped);
		if (tokens.Count == 0)
			return false;

		var trigger = tokens[0].ToLowerInvariant();
		var args = tokens.Skip(1).ToArray();

		return trigger switch
		{
			"help" => await HandleHelpAsync(context, ct),
			"roll" => await HandleRollAsync(context, args, ct),
			"where" => await HandleWhereAsync(context, args, ct),
			"faq" => await HandleFaqAsync(context, args, ct),
			"mp" => await mpCommands.ExecuteAsync(context, args, ct),
			"channel" => await channelCommands.ExecuteAsync(context, args, ct),
			_ => false
		};
	}

	private static async Task<bool> HandleHelpAsync(CommandContext context, CancellationToken ct)
	{
		await context.SayPrivately(CommandCatalog.Lines(context.Replies, ""), ct);
		return true;
	}

	private async Task<bool> HandleRollAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var max = 100;
		if (args.Count > 0 && int.TryParse(args[0], out var parsed) && parsed > 0)
			max = parsed;

		var roll = random.NextInt64(0, (long)max + 1);
		await context.Reply(BotReplies.RollResult, new { user = context.Sender.Value.Name, points = roll },
			ct);
		return true;
	}

	private async Task<bool> HandleWhereAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReplies.WhereUsage, ct);
			return false;
		}

		var name = string.Join(" ", args);
		var user = await users.GetByNameAsync(name, ct);

		if (user is null)
		{
			await context.Reply(BotReplies.NotRegistered, new { name }, ct);
			return true;
		}

		await context.Reply(BotReplies.WhereIsIn,
			new { name = user.Value.Name, country = user.Value.Country.Describe() }, ct);
		return true;
	}

	private async Task<bool> HandleFaqAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReplies.FaqUsage, ct);
			return false;
		}

		var query = string.Join(" ", args);

		if (string.Equals(query, "list", StringComparison.OrdinalIgnoreCase))
		{
			var entries = (await faqs.ListAsync(ct)).ToArray();
			if (entries.Length == 0)
			{
				await context.Reply(BotReplies.NoFaqEntriesAvailable, ct);
				return true;
			}

			await context.Reply(BotReplies.AvailableFaqEntries, new { entries = string.Join(", ", entries) },
				ct);
			return true;
		}

		var reader = await faqs.ReadAsync(query, ct);
		if (reader is null)
		{
			await context.Reply(BotReplies.NoFaqEntryFound, new { entry = query }, ct);
			return true;
		}

		string text;
		using (reader)
		{
			text = await reader.ReadToEndAsync(ct);
		}

		await context.Say(text, ct);
		return true;
	}
}