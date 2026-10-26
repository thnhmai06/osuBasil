using System.Text;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Users;
using Basil.Testing;
using NSubstitute;

namespace Basil.Bot.Tests.Commands;

/// <summary>
///     The chat commands any user can type: which messages are commands, what <c>!help</c>, <c>!roll</c>,
///     <c>!where</c> and <c>!faq</c> answer, and the rules of chained commands. Expected replies are named by the
///     reply that owns the wording.
/// </summary>
public sealed class ChatCommandsTests
{
	[Fact]
	public async Task AMessageWithoutThePrefixInAChannel_IsNotACommand()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "help");

		Assert.False(succeeded);
		Assert.Empty(said.All);
	}

	[Fact]
	public async Task AnUnknownCommand_SaysNothing()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!nonsense");

		Assert.False(succeeded);
		Assert.Empty(said.All);
	}

	[Fact]
	public async Task AMessageToTheBotInPrivate_NeedsNoPrefix()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "help");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(BotReplies.HelpText)], said.Privately);
	}

	[Fact]
	public async Task Help_AnswersInPrivateMessagesEvenWhenTypedInAChannel()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!help");

		Assert.True(succeeded);
		Assert.Empty(said.Here);
		Assert.Equal([bot.Text(BotReplies.HelpText)], said.Privately);
	}

	[Theory]
	[InlineData("!roll", 101)]
	[InlineData("!roll 50", 51)]
	[InlineData("!roll abc", 101)]
	[InlineData("!roll 0", 101)]
	[InlineData("!roll -5", 101)]
	[InlineData("!roll 2147483647", 2147483648)]
	public async Task Roll_RollsFromZeroUpToAndIncludingTheMaximum(string message, long exclusiveUpperBound)
	{
		var dice = new RecordingRandom();
		using var bot = new BotHarness(dice);
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, _) = await bot.TypeAsync(typist, "#osu", message);

		Assert.True(succeeded);
		Assert.Equal((0, exclusiveUpperBound), Assert.Single(dice.Requests));
	}

	[Fact]
	public async Task Roll_SaysWhoRolledAndWhatTheyRolled()
	{
		using var bot = new BotHarness(new RecordingRandom { Result = 42 });
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!roll");

		Assert.Equal([bot.Text(BotReplies.RollResult, new { user = "Typist", points = 42L })], said.Here);
	}

	[Fact]
	public async Task Where_WithoutAName_SaysHowToUseItAndFails()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!where");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(BotReplies.WhereUsage)], said.Here);
	}

	[Fact]
	public async Task Where_ForAnUnregisteredName_SaysTheyAreNotRegistered()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!where Nobody");

		Assert.Equal([bot.Text(BotReplies.NotRegistered, new { name = "Nobody" })], said.Here);
	}

	[Fact]
	public async Task Where_ForARegisteredUser_SaysTheirNameAndCountry()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var other = await bot.App.CreateUserAsync("Other", "password");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!where Other");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(BotReplies.WhereIsIn, new { name = "Other", country = other.Value.Country.Describe() })],
			said.Here);
	}

	[Fact]
	public async Task Faq_WithoutAnEntry_SaysHowToUseItAndFails()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!faq");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(BotReplies.FaqUsage)], said.Here);
	}

	[Fact]
	public async Task Faq_List_WithNoEntries_SaysSo()
	{
		using var bot = new BotHarness();
		bot.Faqs.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<string>>([]));
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!faq list");

		Assert.Equal([bot.Text(BotReplies.NoFaqEntriesAvailable)], said.Here);
	}

	[Fact]
	public async Task Faq_List_NamesEveryEntry()
	{
		using var bot = new BotHarness();
		bot.Faqs.ListAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<string>>(["rules", "mods"]));
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!faq list");

		Assert.Equal([bot.Text(BotReplies.AvailableFaqEntries, new { entries = "rules, mods" })], said.Here);
	}

	[Fact]
	public async Task Faq_ForAMissingEntry_SaysItWasNotFound()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!faq missing");

		Assert.Equal([bot.Text(BotReplies.NoFaqEntryFound, new { entry = "missing" })], said.Here);
	}

	[Fact]
	public async Task Faq_ForAnEntry_SaysItsText()
	{
		using var bot = new BotHarness();
		bot.Faqs.ReadAsync("rules", Arg.Any<CancellationToken>())
			.Returns(new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Be nice."))));
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!faq rules");

		Assert.True(succeeded);
		Assert.Equal(["Be nice."], said.Here);
	}

	[Fact]
	public async Task AChain_InTheLobbyChannel_IsRefused()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#lobby", "!mp lock;!mp unlock");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(BotReplies.ChainNotInLobby)], said.Here);
	}

	[Theory]
	[InlineData("!roll;!roll")]
	[InlineData("!mp lock;!roll")]
	[InlineData("!mp make;!mp lock")]
	[InlineData("!mp lock;!mp join 1")]
	[InlineData("!mp lock;!mp in 1")]
	[InlineData("!mp lock;!mp help")]
	[InlineData("!mp lock;!mp")]
	public async Task AChain_WithACommandThatCannotBeChained_IsRefusedWithoutRunningAnyOfIt(string message)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.Text(BotReplies.ChainOnlyMpAllowed)], said.Here);
	}

	[Fact]
	public async Task ASemicolonChain_RunsTheNextCommandEvenAfterAFailure()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!mp lock;!mp unlock");

		Assert.Equal([bot.Text(MpReplies.NotInARoom), bot.Text(MpReplies.NotInARoom)],
			said.Here);
	}

	[Fact]
	public async Task ADoubleAmpersandChain_StopsAtTheFirstFailure()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp lock && !mp unlock");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NotInARoom)], said.Here);
	}

	/// <summary>Dice that record what they were asked and give a chosen result.</summary>
	private sealed class RecordingRandom : Random
	{
		public List<(long Min, long Max)> Requests { get; } = [];

		public long Result { get; init; }

		public override long NextInt64(long minValue, long maxValue)
		{
			Requests.Add((minValue, maxValue));
			return Result;
		}
	}
}