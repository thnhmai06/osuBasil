using System.Text.Json;

namespace Basil.Application.Services;

/// <summary>
///     Loads the wording for <see cref="Bot.MpReplies" />, <see cref="Irc.IrcReplies" />, and
///     <see cref="Content.ServerReplies" /> from three on-disk localization files, so the text of
///     every user-visible bot/IRC/server reply can be edited without recompiling.
/// </summary>
/// <remarks>
///     Reads <c>Data/Localization/BasilBot.json</c>, <c>Irc.json</c>, and <c>Server.json</c> once,
///     next to the running assembly. Each file is a two-level object -- a category, then a member
///     name within it -- mapping to that reply's text; callers look a value up by its
///     <c>"Category.Member"</c> key. A key missing from its file surfaces as an exception the first
///     time that member is touched -- <c>Program.cs</c> touches one member of each class once at
///     startup specifically so this is a boot-time failure, not one discovered mid-request.
/// </remarks>
internal static class ReplyLocale
{
	private static readonly Lazy<JsonDocument> BasilBotDocument = new(() => Load("BasilBot.json"));
	private static readonly Lazy<JsonDocument> IrcDocument = new(() => Load("Irc.json"));
	private static readonly Lazy<JsonDocument> ServerDocument = new(() => Load("Server.json"));

	/// <summary>Resolves a <see cref="Bot.MpReplies" /> member's text by its <c>"Category.Member"</c> key.</summary>
	public static string BasilBot(string key)
	{
		return Resolve(BasilBotDocument.Value, "BasilBot.json", key);
	}

	/// <summary>Resolves an <see cref="Irc.IrcReplies" /> member's text by its <c>"Category.Member"</c> key.</summary>
	public static string Irc(string key)
	{
		return Resolve(IrcDocument.Value, "Irc.json", key);
	}

	/// <summary>Resolves a <see cref="Content.ServerReplies" /> member's text by its <c>"Category.Member"</c> key.</summary>
	public static string Server(string key)
	{
		return Resolve(ServerDocument.Value, "Server.json", key);
	}

	private static JsonDocument Load(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Data", "Localization", fileName);
		return JsonDocument.Parse(File.ReadAllBytes(path));
	}

	private static string Resolve(JsonDocument document, string fileName, string key)
	{
		var (category, member) = SplitKey(fileName, key);
		var root = document.RootElement;
		if (root.TryGetProperty(category, out var categoryElement) &&
		    categoryElement.TryGetProperty(member, out var valueElement) &&
		    valueElement.GetString() is { } value)
			return value;

		throw new InvalidOperationException($"{fileName} is missing '{key}' (expected {category}.{member}).");
	}

	private static (string Category, string Member) SplitKey(string fileName, string key)
	{
		var separator = key.IndexOf('.');
		if (separator < 0)
			throw new InvalidOperationException(
				$"'{key}' is not a valid {fileName} lookup key -- expected \"Category.Member\".");

		return (key[..separator], key[(separator + 1)..]);
	}
}