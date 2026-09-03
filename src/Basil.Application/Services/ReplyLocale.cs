using System.Text.Json;

namespace Basil.Application.Services;

/// <summary>
///     Loads the wording for <see cref="Bot.MpReplies" /> and <see cref="Irc.IrcReplies" /> from a
///     single on-disk locale file, so the text of every user-visible bot/IRC reply can be edited
///     without recompiling.
/// </summary>
/// <remarks>
///     Reads <c>Data/Locale/replies.json</c> once, next to the running assembly. The file has two
///     top-level objects, <c>Mp</c> and <c>Irc</c>, each mapping a reply's member name (in
///     <see cref="Bot.MpReplies" />/<see cref="Irc.IrcReplies" /> respectively) to its text. A
///     member missing from the file surfaces as an exception the first time that member is touched
///     — <c>Program.cs</c> touches both classes once at startup specifically so this is a boot-time
///     failure, not one discovered mid-request.
/// </remarks>
internal static class ReplyLocale
{
	private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "Data", "Locale", "replies.json");
	private static readonly Lazy<JsonDocument> Document = new(() => JsonDocument.Parse(File.ReadAllBytes(FilePath)));

	/// <summary>Resolves an <see cref="Bot.MpReplies" /> member's text by its member name.</summary>
	public static string Mp(string key)
	{
		return Resolve("Mp", key);
	}

	/// <summary>Resolves an <see cref="Irc.IrcReplies" /> member's text by its member name.</summary>
	public static string Irc(string key)
	{
		return Resolve("Irc", key);
	}

	private static string Resolve(string section, string key)
	{
		var root = Document.Value.RootElement;
		if (root.TryGetProperty(section, out var sectionElement) &&
		    sectionElement.TryGetProperty(key, out var valueElement) &&
		    valueElement.GetString() is { } value)
			return value;

		throw new InvalidOperationException(
			$"Reply locale file is missing '{section}.{key}' (expected at {FilePath}).");
	}
}