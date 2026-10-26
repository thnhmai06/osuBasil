using System.Text.RegularExpressions;
using Basil.Bot.Replies;
using Basil.Bot.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Bot.Tests.Replies;

/// <summary>Contract tests of the replies BasilBot declares: every one can be edited in the replies file.</summary>
public sealed class ReplyDefinitionsTests : IDisposable
{
	private static readonly IReadOnlyList<Reply> All = ReplyDefinitions.In(typeof(Reply).Assembly);

	private static readonly Regex KeyShapePattern = new(@"^(bot|announce|mp)\.[a-z][A-Za-z0-9]*$");

	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), "basil-reply-definitions-" + Guid.NewGuid().ToString("N"));

	public static TheoryData<string> Keys => [.. All.Select(reply => reply.Key)];

	public void Dispose()
	{
		if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
	}

	[Fact]
	public void The_replies_of_the_bot_are_found()
	{
		Assert.NotEmpty(All);
	}

	[Fact]
	public void No_two_replies_share_a_key()
	{
		Assert.Equal(All.Count, All.Select(reply => reply.Key).Distinct(StringComparer.Ordinal).Count());
	}

	[Theory]
	[MemberData(nameof(Keys))]
	public void A_key_names_the_surface_that_says_the_reply_and_the_reply(string key)
	{
		Assert.Matches(KeyShapePattern, key);
	}

	[Theory]
	[MemberData(nameof(Keys))]
	public void A_reply_has_a_default_wording(string key)
	{
		Assert.False(string.IsNullOrWhiteSpace(Find(key).Default));
	}

	[Theory]
	[MemberData(nameof(Keys))]
	public void The_default_wording_of_a_reply_uses_every_argument_the_reply_declares(string key)
	{
		var reply = Find(key);

		foreach (var argument in reply.Arguments)
			Assert.True(MentionsArgument(reply.Default, argument), $"{key} declares {argument} but never uses it");
	}

	[Theory]
	[MemberData(nameof(Keys))]
	public void The_default_wording_of_a_reply_is_valid_message_format_with_its_declared_arguments(string key)
	{
		var reply = Find(key);
		var catalog = NewCatalog();
		var arguments = reply.Arguments.ToDictionary(name => name, _ => (object?)1, StringComparer.Ordinal);

		var text = catalog.Text(reply, arguments);

		Assert.NotNull(text);
	}

	[Fact]
	public void Loading_with_the_replies_of_the_bot_writes_a_file_that_is_read_back_without_a_problem()
	{
		var logger = new CapturingLogger<ReplyCatalog>();
		var catalog = NewCatalog(logger);
		catalog.Load();

		var read = catalog.Reload();

		Assert.True(read);
		Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
	}

	[Fact]
	public void Reading_back_the_file_the_first_load_wrote_leaves_a_reply_without_arguments_with_its_default_wording()
	{
		var catalog = NewCatalog();
		catalog.Load();
		catalog.Reload();

		foreach (var reply in All.Where(reply => reply.Arguments.Length == 0))
			Assert.Equal(reply.Default, catalog.Text(reply));
	}

	private static bool MentionsArgument(string text, string argument)
	{
		return Regex.IsMatch(text, @"\{" + Regex.Escape(argument) + @"\s*[,}]");
	}

	private static Reply Find(string key)
	{
		return All.Single(reply => reply.Key == key);
	}

	private ReplyCatalog NewCatalog(ILogger<ReplyCatalog>? logger = null)
	{
		return new ReplyCatalog(All,
			Options.Create(new RepliesOptions { Path = Path.Combine(_directory, "Bot", "replies.json") }),
			logger ?? NullLogger<ReplyCatalog>.Instance);
	}
}