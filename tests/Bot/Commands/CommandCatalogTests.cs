using System.Text.Json;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Replies;
using Basil.Bot.Tests.Support;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Bot.Tests.Commands;

/// <summary>
///     The catalog of chat commands: the help is made from it, every command in it is one the bot answers, and the
///     replies file can reword what each command is said to do.
/// </summary>
public sealed class CommandCatalogTests
{
	public static TheoryData<string> TopLevelCommands()
	{
		return new TheoryData<string>(CommandCatalog.All.Where(c => !c.Path.Contains(' ')).Select(c => c.Path));
	}

	public static TheoryData<string> MpCommands()
	{
		return new TheoryData<string>(CommandCatalog.All.Where(c => c.Path.StartsWith("mp ")).Select(c => c.Path[3..]));
	}

	[Fact]
	public void TheHelp_ListsACommandAsItsPrefixItsUsageAndWhatItDoes()
	{
		using var bot = new BotHarness();

		var lines = CommandCatalog.Lines(bot.Replies, "").Split('\n');

		Assert.Contains("!roll [max] - roll a random number from 0 to max (default 100)", lines);
		Assert.Contains("!mp help - list multiplayer subcommands", lines);
	}

	[Fact]
	public void TheHelpOfAScope_ListsOnlyItsOwnCommands()
	{
		using var bot = new BotHarness();

		var top = CommandCatalog.Lines(bot.Replies, "").Split('\n');
		var mp = CommandCatalog.Lines(bot.Replies, "mp").Split('\n');

		Assert.DoesNotContain(top, line => line.StartsWith("!mp mods"));
		Assert.Contains("!mp mods <mods>|Freemod|None - set the match mods", mp);
		Assert.All(mp, line => Assert.StartsWith("!mp ", line));
	}

	[Fact]
	public void EveryCommand_IsListedByTheHelpOfItsScope()
	{
		using var bot = new BotHarness();
		var top = CommandCatalog.Lines(bot.Replies, "");
		var mp = CommandCatalog.Lines(bot.Replies, "mp");

		foreach (var command in CommandCatalog.All)
			Assert.Contains($"!{command.Path}", command.Path.Contains(' ') ? mp : top);
	}

	[Theory]
	[MemberData(nameof(TopLevelCommands))]
	public async Task ACommandOfTheCatalog_IsOneTheBotAnswers(string path)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!" + path);

		Assert.True(succeeded || said.All.Any());
	}

	[Theory]
	[MemberData(nameof(MpCommands))]
	public async Task AnMpCommandOfTheCatalog_IsOneTheBotDoesNotCallUnknown(string subcommand)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		await bot.MakeAsync(creator, "Room");

		var (_, said) = await bot.TypeAsync(creator, null, "!mp " + subcommand);

		Assert.DoesNotContain(bot.Text(MpReplies.UnknownMpSubcommand, new { subcommand }), said.All);
	}

	[Fact]
	public async Task TheRepliesFile_RewordsWhatACommandIsSaidToDo()
	{
		var path = Path.Combine(Path.GetTempPath(), $"basil-bot-help-{Guid.NewGuid():N}.json");
		try
		{
			await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string, string>
			{
				["bot.helpRoll"] = "throw a die"
			}), TestContext.Current.CancellationToken);
			var replies = new ReplyCatalog(ReplyDefinitions.In(typeof(Reply).Assembly),
				Options.Create(new RepliesOptions { Path = path }), NullLogger<ReplyCatalog>.Instance);
			replies.Load();

			var lines = CommandCatalog.Lines(replies, "").Split('\n');

			Assert.Contains("!roll [max] - throw a die", lines);
		}
		finally
		{
			File.Delete(path);
		}
	}
}