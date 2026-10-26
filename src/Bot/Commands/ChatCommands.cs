using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Users;
using Basil.Bot.Commands.Mp;
using Basil.Domain.Users;

namespace Basil.Bot.Commands;

/// <summary>Dispatches chat commands to their handlers.</summary>
internal sealed class ChatCommands(IUserRepository users, IFaqStorage faqs, MpCommands mpCommands, Random random)
{
	/// <summary>The text that starts a command.</summary>
	public const string CommandPrefix = "!";

	/// <summary>Executes a command if the text starts with the command prefix.</summary>
	/// <param name="context">The command context.</param>
	/// <param name="text">The message text.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>True if a command was executed; otherwise false (no reply sent for unknown commands).</returns>
	public async Task<bool> ExecuteAsync(CommandContext context, string text, CancellationToken cancellationToken)
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
			if (tokens.Count != 0
			    && string.Equals(tokens[0], "mp", StringComparison.OrdinalIgnoreCase)
			    && tokens.Count >= 2 && MpCommands.IsChainable(tokens[1])) continue;

			await context.Reply(BotReplies.ChainOnlyMpAllowed, cancellationToken);
			return false;
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

	private static string StripPrefix(string segmentText)
	{
		return segmentText.StartsWith(CommandPrefix, StringComparison.Ordinal)
			? segmentText[CommandPrefix.Length..]
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

		var roll = random.NextInt64(0, (long)max + 1);
		await context.Reply(BotReplies.RollResult, new { user = context.Sender.Value.Name, points = roll },
			cancellationToken);
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
			await context.Reply(BotReplies.NotRegistered, new { name }, cancellationToken);
			return true;
		}

		await context.Reply(BotReplies.WhereIsIn,
			new { name = user.Value.Name, country = user.Value.Country.Describe() }, cancellationToken);
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
			var entries = (await faqs.ListAsync(cancellationToken)).ToArray();
			if (entries.Length == 0)
			{
				await context.Reply(BotReplies.NoFaqEntriesAvailable, cancellationToken);
				return true;
			}

			await context.Reply(BotReplies.AvailableFaqEntries, new { entries = string.Join(", ", entries) },
				cancellationToken);
			return true;
		}

		var reader = await faqs.ReadAsync(query, cancellationToken);
		if (reader is null)
		{
			await context.Reply(BotReplies.NoFaqEntryFound, new { entry = query }, cancellationToken);
			return true;
		}

		string text;
		using (reader)
		{
			text = await reader.ReadToEndAsync(cancellationToken);
		}

		await context.Say(text, cancellationToken);
		return true;
	}
}