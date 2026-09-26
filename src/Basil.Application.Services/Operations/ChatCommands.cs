using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Contracts.Storages;
using Basil.Application.Models.Configurations.Options;
using Basil.Application.Models.Queries;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations.Commands.Mp;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>Runs bot and <c>!mp</c> chat commands.</summary>
/// <remarks>
///     Parses <c>!</c>-prefixed text and dispatches to the matching command. <c>!mp</c> subcommands
///     are routed to <see cref="Commands.Mp.MpCommands" />; every other command is handled directly.
/// </remarks>
public sealed class ChatCommands(
	ISettingsRepository settingsRepository,
	IRepository<string, User> usersByName,
	IBlobStorage<string> faqStorage,
	ISearchable<string> faqSearch,
	MpCommands mp,
	ILocalizer localizer)
{
	private static readonly string HelpText = string.Join('\n',
		"!roll [max] - roll a random number from 0 to max (default 100)", "!where <username> - show a user's country",
		"!faq <entry>|list - print a FAQ entry, or list every entry",
		"!mp <subcommand> - multiplayer match control; !mp help for the full list");

	/// <summary>Runs <paramref name="text" /> as a command if it is one.</summary>
	/// <param name="sender">The session that sent the text.</param>
	/// <param name="channel">The channel the text was sent in, or <see langword="null" /> for a direct message.</param>
	/// <param name="text">The text sent.</param>
	/// <param name="cancellationToken">A token that cancels the command.</param>
	/// <returns>The command's reply, or <see langword="null" /> when <paramref name="text" /> is not a command.</returns>
	public async Task<string?> ExecuteAsync(UserSession sender, string? channel, string text,
		CancellationToken cancellationToken = default)
	{
		var prefix = (await settingsRepository.GetAsync<BasilOptions.BotOptions>(cancellationToken)).Prefix;
		if (string.IsNullOrEmpty(prefix) || !text.StartsWith(prefix, StringComparison.Ordinal))
			return null;

		var parts = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return null;

		var trigger = parts[0].ToLowerInvariant();
		var args = parts[1..];

		return trigger switch
		{
			"help" => HelpText,
			"roll" => Roll(sender, args),
			"where" => await WhereAsync(args, cancellationToken),
			"faq" => await FaqAsync(args, cancellationToken),
			"mp" => await mp.ExecuteAsync(sender, args, cancellationToken),
			_ => null
		};
	}

	private static string Roll(UserSession sender, string[] args)
	{
		var max = 100;
		if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) max = parsed;

		var roll = (int)Random.Shared.NextInt64(0, (long)max + 1);
		return $"{sender.UserId} rolls {roll} point(s)";
	}

	private async Task<string> WhereAsync(string[] args, CancellationToken cancellationToken)
	{
		if (args.Length < 1) return localizer.Get(BotReplies.WhereUsage);

		var name = string.Join(' ', args);
		var user = await usersByName.LoadAsync(UserSafeName.Of(name), cancellationToken);
		return user is null
			? localizer.Get(BotReplies.NotRegistered, name)
			: localizer.Get(BotReplies.WhereIsIn, user.Name, user.Country.Describe());
	}

	private async Task<string> FaqAsync(string[] args, CancellationToken cancellationToken)
	{
		if (args.Length < 1) return localizer.Get(BotReplies.FaqUsage);

		if (args.Length == 1 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
		{
			var entries = await faqSearch.SearchAsync(new FaqQuery(), cancellationToken: cancellationToken)
				.ToListAsync(cancellationToken);
			return entries.Count == 0
				? localizer.Get(BotReplies.NoFaqEntriesAvailable)
				: localizer.Get(BotReplies.AvailableFaqEntries, string.Join(", ", entries));
		}

		var requested = string.Join(' ', args);
		await using var content = await faqStorage.OpenReadAsync(requested, cancellationToken);
		if (content is null) return localizer.Get(BotReplies.NoFaqEntryFound, requested);

		using var reader = new StreamReader(content);
		return await reader.ReadToEndAsync(cancellationToken);
	}
}