using Basil.Application.Bot.Replies;
using Basil.Application.Bot.Commands;
using Basil.Application.Chat.Objects;
using Basil.Application.Content.Storage;
using Basil.Application.Users.Persistence;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;

namespace Basil.Infrastructure.Bot.Handlers.Commands;

internal sealed class ChatCommands(
	IUserRepository users,
	IFaqStorage faqs,
	MpCommands mpCommands,
	ChannelCommands channelCommands,
	Random random)
{
	public const string CommandPrefix = "!";

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

	private static async Task<bool> IsChainAllowedAsync(CommandContext context, IReadOnlyList<CommandSegment> segments,
		CancellationToken ct)
	{
		if (context.Channel == GeneralChannel.LobbyName)
		{
			await context.Reply(BotReply.BotChainNotInLobby, ct);
			return false;
		}

		foreach (var segment in segments)
		{
			var stripped = StripPrefix(segment.Text);
			var tokens = CommandLine.Tokenize(stripped);
			if (tokens.Count != 0
			    && string.Equals(tokens[0], "mp", StringComparison.OrdinalIgnoreCase)
			    && tokens.Count >= 2 && MpCommands.IsChainable(tokens[1])) continue;

			await context.Reply(BotReply.BotChainOnlyMpAllowed, ct);
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
			"help" => await HandleHelpAsync(context, args, ct),
			"roll" => await HandleRollAsync(context, args, ct),
			"where" => await HandleWhereAsync(context, args, ct),
			"faq" => await HandleFaqAsync(context, args, ct),
			"mp" => await mpCommands.ExecuteAsync(context, args, ct),
			"channel" => await channelCommands.ExecuteAsync(context, args, ct),
			_ => await UnknownAsync(context, tokens[0], ct)
		};
	}

	private static async Task<bool> UnknownAsync(CommandContext context, string command, CancellationToken ct)
	{
		await context.ReplyToUnknownCommand(BotReply.BotUnknownCommand, new { command }, ct);
		return false;
	}

	private static async Task<bool> HandleHelpAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count == 0)
		{
			await context.SayPrivately(CommandCatalog.Lines(context.Replies, ""), ct);
			return true;
		}

		var topic = args[0].TrimStart('!').ToLowerInvariant();
		if (topic == "mp")
		{
			await context.SayPrivately(CommandCatalog.Lines(context.Replies, "mp"), ct);
			return true;
		}

		if (CommandCatalog.Line(context.Replies, topic) is not { } line)
		{
			await context.ReplyPrivately(BotReply.BotUnknownCommand, new { command = args[0].TrimStart('!') }, ct);
			return false;
		}

		await context.SayPrivately(line, ct);
		return true;
	}

	private async Task<bool> HandleRollAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		var max = 100;
		if (args.Count > 0)
		{
			if (!int.TryParse(args[0], out max) || max <= 0)
			{
				await context.Reply(BotReply.BotRollUsage, ct);
				return false;
			}
		}

		var roll = random.NextInt64(0, (long)max + 1);
		await context.Reply(BotReply.BotRollResult, new { user = context.Sender.Value.Name, points = roll },
			ct);
		return true;
	}

	private async Task<bool> HandleWhereAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReply.BotWhereUsage, ct);
			return false;
		}

		var name = string.Join(" ", args);
		var user = await users.GetByNameAsync(name, ct);

		if (user is null)
		{
			await context.Reply(BotReply.MpUserNotFound, ct);
			return true;
		}

		if (user.Value.Country == Country.Xx)
		{
			await context.Reply(BotReply.BotWhereUnknownCountry, new { name = user.Value.Name }, ct);
			return true;
		}

		await context.Reply(BotReply.BotWhereIsIn,
			new { name = user.Value.Name, country = user.Value.Country.Describe() }, ct);
		return true;
	}

	private async Task<bool> HandleFaqAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken ct)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReply.BotFaqUsage, ct);
			return false;
		}

		var query = string.Join(" ", args);

		if (string.Equals(query, "list", StringComparison.OrdinalIgnoreCase))
		{
			var entries = (await faqs.ListAsync(ct)).ToArray();
			if (entries.Length == 0)
			{
				await context.Reply(BotReply.BotNoFaqEntriesAvailable, ct);
				return true;
			}

			await context.Reply(BotReply.BotAvailableFaqEntries, new { entries = string.Join(", ", entries) },
				ct);
			return true;
		}

		var reader = FaqRules.IsValidName(query) ? await faqs.ReadAsync(query, ct) : null;
		if (reader is null)
		{
			await context.Reply(BotReply.BotNoFaqEntryFound, new { entry = query }, ct);
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