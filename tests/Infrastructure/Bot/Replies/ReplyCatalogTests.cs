using System.Text.Json;
using Basil.Application.Bot.Replies;
using Basil.Infrastructure.Bot.Services;
using Basil.Infrastructure.Bot.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Bot.Tests.Replies;

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
	public void NoRepliesFile_DefaultWordingFilled()
	{
		var catalog = NewCatalog();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void ReplyWithoutArguments_NeedsNone()
	{
		var catalog = NewCatalog();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Theory]
	[InlineData(1, "1 map found")]
	[InlineData(2, "2 maps found")]
	[InlineData(0, "0 maps found")]
	public void Plural_PicksNumberForm(int count, string expected)
	{
		var catalog = NewCatalog();

		Assert.Equal(expected, catalog.Text(Count, new { count }));
	}

	[Fact]
	public void MissingDeclaredArgument_CannotSay()
	{
		var catalog = NewCatalog();

		Assert.Throws<ArgumentException>(() => catalog.Text(Joined, new { room = 5 }));
	}

	[Fact]
	public void DictionaryArguments_Accepted()
	{
		var catalog = NewCatalog();

		var text = catalog.Text(Joined, new Dictionary<string, object?> { ["room"] = 5, ["name"] = "Finals" });

		Assert.Equal("Joined match #5 Finals", text);
	}

	[Fact]
	public void NoRepliesFile_WritesEveryDefault()
	{
		var catalog = NewCatalog();

		catalog.Load();

		var written = ReadFile();
		Assert.Equal(Joined.Default, written[Joined.Key]);
		Assert.Equal(Plain.Default, written[Plain.Key]);
		Assert.Equal(Count.Default, written[Count.Key]);
	}

	[Fact]
	public void FirstWrittenFile_EveryReplyDefault()
	{
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
		Assert.Equal("1 map found", catalog.Text(Count, new { count = 1 }));
	}

	[Fact]
	public void Loading_KeepsExistingFile()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal(["test.plain"], ReadFile().Keys);
	}

	[Fact]
	public void FileText_ReplacesDefault()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{name} (match {room}) is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Finals (match 5) is open", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void UnmentionedReply_KeepsDefault()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{name} is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void Text_MayOmitArguments()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "A match is open" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("A match is open", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void Text_MayUsePlurals()
	{
		WriteFile(new Dictionary<string, string>
			{ [Count.Key] = "{count, plural, =0 {nothing} one {a map} other {# maps}}" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal(["nothing", "a map", "3 maps"],
			new[] { 0, 1, 3 }.Select(count => catalog.Text(Count, new { count })));
	}

	[Fact]
	public void UnknownKeyText_Ignored()
	{
		WriteFile(new Dictionary<string, string> { ["test.unknown"] = "hello", [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void UndeclaredArgumentText_KeepsDefault()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {server}" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void UnusableText_ReportedAtLoad()
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
	public void UnparsableFile_Reported()
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
	public void InvalidMessageFormat_KeepsDefault()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {room" });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Joined match #5 Finals", catalog.Text(Joined, new { room = 5, name = "Finals" }));
	}

	[Fact]
	public void InvalidText_OtherTextsApply()
	{
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "Joined {room", [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void NonStringValue_KeepsDefault()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, """{ "test.plain": 5 }""");
		var catalog = NewCatalog();

		catalog.Load();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void FillInFailure_SaysDefault()
	{
		// Valid with the numbers the file is checked with, but the room is given as text.
		WriteFile(new Dictionary<string, string> { [Joined.Key] = "{room, plural, one {# room} other {# rooms}}" });
		var catalog = NewCatalog();
		catalog.Load();

		var text = catalog.Text(Joined, new { room = "not a number", name = "Finals" });

		Assert.Equal("Joined match #not a number Finals", text);
	}

	[Fact]
	public void Reloading_TakesNewWording()
	{
		var catalog = NewCatalog();
		catalog.Load();
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });

		var read = catalog.Reload();

		Assert.True(read);
		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void ReloadWithoutText_ReturnsDefault()
	{
		WriteFile(new Dictionary<string, string> { [Plain.Key] = "Not in a match." });
		var catalog = NewCatalog();
		catalog.Load();
		WriteFile(new Dictionary<string, string>());

		catalog.Reload();

		Assert.Equal("You're not in a match.", catalog.Text(Plain));
	}

	[Fact]
	public void UnparsableReload_KeepsWording()
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
	public void EmptyObjectFile_KeepsWording()
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
	public void DeletedFile_KeepsWording()
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
	public void UnwritableFolder_KeepsDefault()
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
	public void ApostropheText_SaidAsWritten()
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