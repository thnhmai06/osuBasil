using Basil.Bot.Application.Basil;
using Basil.Bot.Application.Commands.Mp;
using Basil.Domain.Users;
using Microsoft.Extensions.Options;

namespace Basil.Bot.Application.Commands;

/// <summary>Dispatches chat commands to their handlers.</summary>
internal sealed class ChatCommands(
	IOptions<BotOptions> options,
	IBasilUsers users,
	IBasilFaqs faqs,
	MpCommands mpCommands,
	Random random)
{
	/// <summary>Executes a command if the text matches the configured prefix.</summary>
	/// <param name="context">The command context.</param>
	/// <param name="text">The message text.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>True if a command was executed; otherwise false (no reply sent for unknown commands).</returns>
	public async Task<bool> ExecuteAsync(CommandContext context, string text, CancellationToken cancellationToken)
	{
		var prefix = options.Value.Prefix;
		if (string.IsNullOrEmpty(text))
			return false;

		var isPrivate = context.Channel is null;
		var hasPrefix = text.StartsWith(prefix, StringComparison.Ordinal);

		if (!hasPrefix && !isPrivate)
			return false;

		// Rebuild with prefix so the splitter sees every segment as a full command
		var commandText = hasPrefix ? text : prefix + text;
		var segments = CommandLine.Split(commandText);

		if (segments.Count == 1)
			return await ExecuteSingleAsync(context, segments[0].Text, cancellationToken);

		// ── Chain validation ─────────────────────────────────────────────────────────────
		if (context.Channel == "#lobby")
		{
			await context.Reply(BotReplies.ChainNotInLobby, cancellationToken);
			return false;
		}

		foreach (var segment in segments)
		{
			var stripped = StripPrefix(segment.Text);
			var tokens = CommandLine.Tokenize(stripped);
			if (tokens.Count == 0)
			{
				await context.Reply(BotReplies.ChainOnlyMpAllowed, cancellationToken);
				return false;
			}

			if (!string.Equals(tokens[0], "mp", StringComparison.OrdinalIgnoreCase))
			{
				await context.Reply(BotReplies.ChainOnlyMpAllowed, cancellationToken);
				return false;
			}

			if (tokens.Count < 2 || !MpCommands.IsChainable(tokens[1]))
			{
				await context.Reply(BotReplies.ChainOnlyMpAllowed, cancellationToken);
				return false;
			}
		}

		// ── Chain execution ──────────────────────────────────────────────────────────────
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
			succeeded = await mpCommands.ExecuteAsync(context, args, cancellationToken);
		}

		return succeeded;
	}

	private string StripPrefix(string segmentText)
	{
		var prefix = options.Value.Prefix;
		return segmentText.StartsWith(prefix, StringComparison.Ordinal)
			? segmentText[prefix.Length..]
			: segmentText;
	}

	private async Task<bool> ExecuteSingleAsync(CommandContext context, string segmentText,
		CancellationToken cancellationToken)
	{
		var stripped = StripPrefix(segmentText);
		var tokens = CommandLine.Tokenize(stripped);
		if (tokens.Count == 0)
			return false;

		var trigger = tokens[0].ToLowerInvariant();
		var args = tokens.Skip(1).ToArray();

		return trigger switch
		{
			"help" => await HandleHelpAsync(context, cancellationToken),
			"roll" => await HandleRollAsync(context, args, cancellationToken),
			"where" => await HandleWhereAsync(context, args, cancellationToken),
			"faq" => await HandleFaqAsync(context, args, cancellationToken),
			"mp" => await mpCommands.ExecuteAsync(context, args, cancellationToken),
			_ => false
		};
	}

	private static async Task<bool> HandleHelpAsync(CommandContext context, CancellationToken cancellationToken)
	{
		await context.ReplyPrivately(BotReplies.HelpText, cancellationToken);
		return true;
	}

	private async Task<bool> HandleRollAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var max = 100;
		if (args.Count > 0 && int.TryParse(args[0], out var parsed) && parsed > 0)
			max = parsed;

		var roll = random.Next(0, max + 1);
		await context.Reply(string.Format(BotReplies.RollResult, context.Sender.Value.Name, roll), cancellationToken);
		return true;
	}

	private async Task<bool> HandleWhereAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReplies.WhereUsage, cancellationToken);
			return false;
		}

		var name = string.Join(" ", args);
		var user = await users.GetByNameAsync(name, cancellationToken);

		if (user is null)
		{
			await context.Reply(string.Format(BotReplies.NotRegistered, name), cancellationToken);
			return true;
		}

		await context.Reply(string.Format(BotReplies.WhereIsIn, user.Value.Name, user.Value.Country.Describe()), cancellationToken);
		return true;
	}

	private async Task<bool> HandleFaqAsync(CommandContext context, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count == 0)
		{
			await context.Reply(BotReplies.FaqUsage, cancellationToken);
			return false;
		}

		var query = string.Join(" ", args);

		if (string.Equals(query, "list", StringComparison.OrdinalIgnoreCase))
		{
			var entries = await faqs.ListAsync(cancellationToken);
			if (entries.Count == 0)
			{
				await context.Reply(BotReplies.NoFaqEntriesAvailable, cancellationToken);
				return true;
			}

			await context.Reply(string.Format(BotReplies.AvailableFaqEntries, string.Join(", ", entries)),
				cancellationToken);
			return true;
		}

		var text = await faqs.ReadAsync(query, cancellationToken);
		if (text is null)
		{
			await context.Reply(string.Format(BotReplies.NoFaqEntryFound, query), cancellationToken);
			return true;
		}

		await context.Reply(text, cancellationToken);
		return true;
	}
}
