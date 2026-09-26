namespace Basil.Application.Contracts.Ports;

/// <summary>Resolves a localized message for a key.</summary>
public interface ILocalizer
{
	/// <summary>Gets the localized text for <paramref name="key" />, formatted with <paramref name="args" />.</summary>
	/// <param name="key">The message key to resolve.</param>
	/// <param name="args">The values to format the message with.</param>
	/// <returns>The formatted, localized text, or <paramref name="key" /> itself when the key is not found.</returns>
	string Get(string key, params object[] args);
}