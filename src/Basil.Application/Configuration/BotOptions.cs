namespace Basil.Application.Configuration;

/// <summary>
///     Configuration for the built-in chat bot account that answers chat and <c>!mp</c> commands.
/// </summary>
/// <remarks>
///     <see cref="Basil.Application.Services.Bot.BotBootstrapService" /> boots the seeded account row
///     (id 0) into an in-memory session at startup and applies these options to it: the configured
///     <see cref="Name" /> renames the row when it differs, and <see cref="Country" /> updates its
///     stored country code. The name is configurable so an unrestricted rename cannot collide with
///     the account's originally seeded row.
/// </remarks>
public sealed class BotOptions
{
	public const string SectionName = "Basil:Bot";

	/// <summary>Gets or sets the display name of the built-in chat bot account.</summary>
	/// <value>Defaults to <c>BasilBot</c>.</value>
	public string Name { get; init; } = "BasilBot";

	/// <summary>
	///     Gets or sets the prefix chat commands must start with, for example <c>!</c> for
	///     <c>!help</c>, <c>!roll</c>, and <c>!mp</c>.
	/// </summary>
	public required string CommandPrefix { get; init; }

	/// <summary>Gets or sets the two-letter country code of the built-in chat bot account.</summary>
	/// <value>Defaults to <c>vn</c>.</value>
	public string Country { get; init; } = "vn";
}