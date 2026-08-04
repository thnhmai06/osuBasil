namespace Basil.Application.Abstractions.Content;

/// <summary>
///     Provides the current message-of-the-day text shown to a player on login.
/// </summary>
public interface IMotdProvider
{
	/// <summary>Gets the current MOTD text, or <see langword="null" /> when none is configured.</summary>
	string? GetText();
}
