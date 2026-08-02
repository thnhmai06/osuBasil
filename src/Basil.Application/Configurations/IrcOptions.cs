namespace Basil.Application.Configurations;

/// <summary>
///     Configuration for the embedded IRC gateway.
/// </summary>
/// <remarks>
///     Chat arriving over IRC is routed through the same channel and dispatch services as the osu!
///     binary protocol, so both transports share a single chat core (see docs/architecture.md).
/// </remarks>
public sealed class IrcOptions
{
	public const string SectionName = "Basil:Irc";

	/// <summary>Gets or sets the display name the IRC gateway announces for itself.</summary>
	/// <value>Defaults to <c>Basil</c>.</value>
	public string Name { get; init; } = "Basil";

	/// <summary>Gets or sets the TCP port the IRC gateway listens on.</summary>
	/// <value>Defaults to <c>6667</c>.</value>
	public int Port { get; init; } = 6667;
}