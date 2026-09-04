using System.Reflection;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Irc;

namespace Basil.Application.Tests.Services;

/// <summary>
///     Covers the localization files' alignment with <see cref="MpReplies" />/<see cref="IrcReplies" />:
///     every public member resolves to non-empty text. Each class's own static constructor already
///     fails loudly -- an exception on first touch of the class -- when one of its own keys is missing
///     from its localization file; this test catches a resolved-but-blank value, which that mechanism
///     alone would not.
/// </summary>
public class ReplyLocaleTests
{
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
}