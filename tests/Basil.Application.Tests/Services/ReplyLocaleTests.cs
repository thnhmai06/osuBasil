using System.Reflection;
using System.Text.Json;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Irc;

namespace Basil.Application.Tests.Services;

/// <summary>
///     Covers the reply locale file's alignment with <see cref="MpReplies" />/<see cref="IrcReplies" />:
///     every public member resolves to non-empty text, and the file carries no orphaned key that no
///     member reads. <c>MpReplies</c>/<c>IrcReplies</c> already fail loudly (an exception on first
///     touch of the class) when a member's key is missing from the file — these tests catch drift in
///     the file itself, in either direction.
/// </summary>
public class ReplyLocaleTests
{
	private static JsonElement LoadSection(string section)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Data", "Locale", "replies.json");
		using var document = JsonDocument.Parse(File.ReadAllBytes(path));
		return document.RootElement.GetProperty(section).Clone();
	}

	private static IEnumerable<string> PublicStaticStringFieldNames(Type type)
	{
		return type.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(string))
			.Select(f => f.Name);
	}

	[Fact]
	public void MpReplies_EveryMemberResolvesToNonEmptyText()
	{
		foreach (var field in typeof(MpReplies).GetFields(BindingFlags.Public | BindingFlags.Static))
			Assert.False(string.IsNullOrEmpty((string?)field.GetValue(null)), $"MpReplies.{field.Name}");
	}

	[Fact]
	public void IrcReplies_EveryMemberResolvesToNonEmptyText()
	{
		foreach (var field in typeof(IrcReplies).GetFields(BindingFlags.Public | BindingFlags.Static))
			Assert.False(string.IsNullOrEmpty((string?)field.GetValue(null)), $"IrcReplies.{field.Name}");
	}

	[Fact]
	public void RepliesJson_MpSection_HasNoKeyUnusedByMpReplies()
	{
		var memberNames = PublicStaticStringFieldNames(typeof(MpReplies)).ToHashSet();
		var fileKeys = LoadSection("Mp").EnumerateObject().Select(p => p.Name);

		var orphaned = fileKeys.Where(k => !memberNames.Contains(k)).ToList();
		Assert.True(orphaned.Count == 0, $"Orphaned Mp keys in replies.json: {string.Join(", ", orphaned)}");
	}

	[Fact]
	public void RepliesJson_IrcSection_HasNoKeyUnusedByIrcReplies()
	{
		var memberNames = PublicStaticStringFieldNames(typeof(IrcReplies)).ToHashSet();
		var fileKeys = LoadSection("Irc").EnumerateObject().Select(p => p.Name);

		var orphaned = fileKeys.Where(k => !memberNames.Contains(k)).ToList();
		Assert.True(orphaned.Count == 0, $"Orphaned Irc keys in replies.json: {string.Join(", ", orphaned)}");
	}
}
