using System.Text.Json;
using Basil.Bot.Replies;
using Basil.Bot.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Bot.Tests.Replies;

/// <summary>Contract tests of the wording of BasilBot's replies: the defaults, and what the replies file changes.</summary>
public sealed class ReplyCatalogTests : IDisposable
{
	private static readonly Reply Joined = new("test.joined", "Joined match #{room} {name}", "room", "name");
	private static readonly Reply Plain = new("test.plain", "You're not in a match.");

	private static readonly Reply Count = new("test.count", "{count, plural, one {# map} other {# maps}} found",
		"count");

	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), "basil-replies-" + Guid.NewGuid().ToString("N"));

	private string FilePath => Path.Combine(_directory, "Bot", "replies.json");

	public void Dispose()
	{
		if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
	}

	[Fact]
	public void A_reply_without_a_replies_file_has_its_default_wording_with_the_arguments_filled_in()
	{
		var catalog = NewCatalog();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void A_reply_without_arguments_needs_none()
	{
		var catalog = NewCatalog();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Theory]
	[InlineData(1, "1 map found")]
	[InlineData(2, "2 maps found")]
	[InlineData(0, "0 maps found")]
	public void A_plural_picks_the_form_for_the_number(int count, string expected)
	{
		var catalog = NewCatalog();

		Assert.Equal(expected, catalog.Text(Count, new { count }));
	}

	[Fact]
	public void A_reply_cannot_be_said_without_an_argument_it_declares()
	{
		var catalog = NewCatalog();

		Assert.Throws<ArgumentException>(() => catalog.Text(Joined, new { room = 5 }));
	}

	[Fact]
	public void Arguments_can_be_given_as_a_dictionary()
	{
		var catalog = NewCatalog();

		var text = catalog.Text(Joined, new Dictionary<string, object?> { ["room"] = 5, ["name"] = "Finals" });

		Assert.Equal("Joined match #5 Finals", text);
	}

	[Fact]
	public void Loading_without_a_replies_file_writes_one_with_the_default_wording_of_every_reply()
	{
		var catalog = NewCatalog();

		catalog.Load();

		var written = ReadFile();
		Assert.Equal(Joined.Default, written[Joined.Key]);
		Assert.Equal(Plain.Default, written[Plain.Key]);
		Assert.Equal(Count.Default, written[Count.Key]);
	}

	[Fact]
	public void The_file_written_at_the_first_load_leaves_every_reply_with_its_default_wording()
	{
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
		Assert.Equal("1 map found", catalog.Text(Count, new { count = 1 }));
	}

	[Fact]
	public void Loading_keeps_a_replies_file_that_already_exists()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal(["test.plain"], ReadFile().Keys);
	}

	[Fact]
	public void A_text_in_the_file_replaces_the_default_wording()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{name} (match {room}) is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Finals (match 5) is open", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void A_reply_the_file_does_not_mention_keeps_its_default_wording()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{name} is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_text_may_leave_out_arguments_of_its_reply()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "A match is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("A match is open", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void A_text_may_use_plurals()
	{
		WriteFile(new Dictionary<string, string>
			{ [Count.Key] = "{count, plural, =0 {nothing} one {a map} other {# maps}}" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal(["nothing", "a map", "3 maps"],
			new[] { 0, 1, 3 }.Select(count => catalog.Text(Count, new { count })));
	}

	[Fact]
	public void A_text_for_a_key_that_is_no_reply_is_ignored()
	{
		WriteFile(new Dictionary<string, string> { ["test.unknown"] = "hello", [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_text_that_uses_an_argument_its_reply_does_not_declare_leaves_the_default_wording()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {server}" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void A_text_that_cannot_be_used_is_reported_when_the_file_is_read_not_when_it_is_first_said()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {server}" });
		var logger = new CapturingLogger<ReplyCatalog>();
		var catalog = NewCatalog(logger);

		catalog.Load();

		var entry = Assert.Single(logger.Entries);
		Assert.Equal(LogLevel.Warning, entry.Level);
		Assert.Contains(Joined.Key, entry.Message);
	}

	[Fact]
	public void A_file_that_cannot_be_parsed_is_reported()
	{
		WriteFile(new Dictionary<string, string>());
		var logger = new CapturingLogger<ReplyCatalog>();
		var catalog = NewCatalog(logger);
		catalog.Load();
		File.WriteAllText(FilePath, "{ not json");

		catalog.Reload();

		Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
	}

	[Fact]
	public void A_text_that_is_not_valid_message_format_leaves_the_default_wording()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {room" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void An_invalid_text_does_not_stop_the_valid_texts_of_the_same_file()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {room", [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_value_that_is_not_a_string_leaves_the_default_wording()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, """{ "test.plain": 5 }""");
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_text_that_fails_when_it_is_filled_in_says_the_default_wording_instead()
	{
		// Valid with the numbers the file is checked with, but the room is given as text.
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{room, plural, one {# room} other {# rooms}}" });
		var catalog = NewCatalog();
		catalog.Load();

		var text = catalog.Text(Joined, new { room = "not a number", name = "Finals" });

		Assert.Equal("Joined match #not a number Finals", text);
	}

	[Fact]
	public void Reloading_takes_the_new_wording_of_the_file()
	{
		var catalog = NewCatalog();
		catalog.Load();
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });

		var read = catalog.Reload();

		Assert.True(read);
		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void Reloading_a_file_without_a_text_returns_that_reply_to_its_default_wording()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();
		catalog.Load();
		WriteFile(new Dictionary<string, string>());

		catalog.Reload();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_file_that_cannot_be_parsed_leaves_the_wording_that_was_in_use()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();
		catalog.Load();
		File.WriteAllText(FilePath, """{ "test.plain": "half written""");

		var read = catalog.Reload();

		Assert.False(read);
		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_file_that_holds_no_object_leaves_the_wording_that_was_in_use()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();
		catalog.Load();
		File.WriteAllText(FilePath, "[]");

		var read = catalog.Reload();

		Assert.False(read);
		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_file_that_has_been_deleted_leaves_the_wording_that_was_in_use()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();
		catalog.Load();
		File.Delete(FilePath);

		var read = catalog.Reload();

		Assert.False(read);
		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void A_replies_file_in_a_folder_that_cannot_be_written_leaves_the_default_wording()
	{
		var blocked = Path.Combine(_directory, "blocked");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(blocked, "a file where the folder should be");
		var catalog = new ReplyCatalog([Joined, Plain, Count],
			Options.Create(new RepliesOptions { Path = Path.Combine(blocked, "replies.json") }),
			NullLogger<ReplyCatalog>.Instance);

		catalog.Load();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void An_apostrophe_in_a_text_is_said_as_it_is_written()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "You aren't in a match, and it's fine." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("You aren't in a match, and it's fine.", catalog.Text(Plain));
	}

	private ReplyCatalog NewCatalog(ILogger<ReplyCatalog>? logger = null)
	{
		return new ReplyCatalog([Joined, Plain, Count], Options.Create(new RepliesOptions { Path = FilePath }),
			logger ?? NullLogger<ReplyCatalog>.Instance);
	}

	private void WriteFile(Dictionary<string, string> texts)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, JsonSerializer.Serialize(texts));
	}

	private Dictionary<string, string> ReadFile()
	{
		return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))!;
	}
}