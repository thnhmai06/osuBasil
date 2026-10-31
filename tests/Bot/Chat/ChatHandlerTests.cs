using System.Text;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Bot.Chat;
using Basil.Bot.Commands;
using Basil.Bot.Tests.Support;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Bot.Tests.Chat;

/// <summary>
///     Which channel messages the bot answers as commands, where it answers them, and how long answers are posted.
///     Every case hands the handler a message, waits until the worker has run everything the handler queued, and then
///     looks at what the server said.
/// </summary>
public sealed class ChatHandlerTests : IAsyncLifetime
{
	private const int MaxPieceLength = 2000;

	private readonly BotHarness _bot = new();
	private readonly ChatHandler _handler;
	private readonly CommandWorker _worker = new(NullLogger<CommandWorker>.Instance, TimeProvider.System);
	private BotConnection _botConnection = null!;
	private GeneralChannel _channel = null!;
	private BanchoConnection _typist = null!;

	public ChatHandlerTests()
	{
		_handler = new ChatHandler(_bot.App.Server, _bot.Chat, _worker, _bot.Replies,
			NullLogger<ChatHandler>.Instance);
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		await _worker.StartAsync(Token);
		_botConnection = await _bot.StartBotAsync();
		_channel = await _bot.OpenChannelAsync("#osu");
		_typist = await RoomFixture.PlayerAsync(_bot.App, "Typist");
	}

	public async ValueTask DisposeAsync()
	{
		await _worker.StopAsync(CancellationToken.None);
		_worker.Dispose();
		_bot.Dispose();
	}

	/// <summary>Has the typist post a message and waits until the worker has run what the handler queued.</summary>
	private async Task PostAsync(Channel channel, string content, bool isNotice = false, UserRecord? author = null)
	{
		var message = new Message
		{
			Author = author ?? _typist.Record, Content = content, Timestamp = _bot.App.Clock.GetUtcNow(),
			IsNotice = isNotice
		};

		await _handler.Handle(new ChannelMessagePosted(channel, message, false, null, _typist), Token);
		await DrainAsync();
	}

	/// <summary>Waits until every command queued so far has run, by queueing one more behind them.</summary>
	private async Task DrainAsync()
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.True(_worker.Enqueue(int.MaxValue, _ =>
		{
			done.SetResult();
			return Task.CompletedTask;
		}));
		await done.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
	}

	private List<ChannelMessagePosted> Posts()
	{
		return [.. _bot.App.ChannelEvents.Events.OfType<ChannelMessagePosted>()];
	}

	private void GivenFaq(string entry, string text)
	{
		_bot.Faqs.ReadAsync(entry, Arg.Any<CancellationToken>())
			.Returns(new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text))));
	}

	private void GivenFailingFaq(string entry)
	{
		_bot.Faqs.ReadAsync(entry, Arg.Any<CancellationToken>())
			.Returns(Task.FromException<StreamReader?>(new InvalidOperationException()));
	}

	/// <summary>Gets the non-empty lines of a reply, which is how the bot posts a reply of several lines.</summary>
	private static List<string> Lines(string text)
	{
		return [.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
	}

	#region Which messages are commands

	[Fact]
	public async Task AMessageWithoutThePrefixInAChannel_IsNotAnswered()
	{
		await PostAsync(_channel, "hello everyone");

		Assert.Empty(Posts());
	}

	[Fact]
	public async Task Help_InAChannel_IsWhisperedToTheTypistAndNotPostedInTheChannel()
	{
		await PostAsync(_channel, "!help");

		Assert.Equal(Lines(_bot.Text(BotReplies.HelpText)), _bot.WhisperedTo(_typist));
		Assert.Empty(_bot.SaidIn(_channel));
	}

	[Fact]
	public async Task Roll_InAChannel_IsSaidInThatChannel()
	{
		await PostAsync(_channel, "!roll");

		Assert.Equal([_bot.Text(BotReplies.RollResult, new { user = "Typist", points = 0L })], _bot.SaidIn(_channel));
		Assert.Empty(_bot.WhisperedTo(_typist));
	}

	[Fact]
	public async Task AMessageToTheBotInPrivate_NeedsNoPrefix()
	{
		await PostAsync(_bot.App.PmChannels.Of(_botConnection.User), "help");

		Assert.Equal(Lines(_bot.Text(BotReplies.HelpText)), _bot.WhisperedTo(_typist));
	}

	[Fact]
	public async Task AMessageToTheBotInPrivate_IsAnsweredByWhisperAndNotPostedInTheBotsChannel()
	{
		await PostAsync(_bot.App.PmChannels.Of(_botConnection.User), "roll");

		Assert.Equal([_bot.Text(BotReplies.RollResult, new { user = "Typist", points = 0L })],
			_bot.WhisperedTo(_typist));
		Assert.Empty(_bot.SaidIn(_bot.App.PmChannels.Of(_botConnection.User)));
	}

	[Fact]
	public async Task APrivateMessageToAnotherUser_IsIgnored()
	{
		var other = await RoomFixture.PlayerAsync(_bot.App, "Other");

		await PostAsync(_bot.App.PmChannels.Of(other.User), "!roll");

		Assert.Empty(Posts());
	}

	[Fact]
	public async Task ANotice_IsIgnored()
	{
		await PostAsync(_channel, "!roll", true);

		Assert.Empty(Posts());
	}

	[Theory]
	[InlineData("   ")]
	[InlineData("\t")]
	public async Task AWhitespaceMessage_IsRunAsACommandLineAndGetsNoReply(string content)
	{
		await PostAsync(_bot.App.PmChannels.Of(_botConnection.User), content);

		Assert.Empty(Posts());
	}

	[Fact]
	public async Task AMessageAuthoredByTheBotItself_IsIgnored()
	{
		await PostAsync(_channel, "!roll", author: _botConnection.Record);

		Assert.Empty(Posts());
	}

	[Fact]
	public async Task AMessageTheServerSaidItself_IsIgnored()
	{
		var message = new Message
			{ Author = _typist.Record, Content = "!roll", Timestamp = _bot.App.Clock.GetUtcNow() };

		await _handler.Handle(new ChannelMessagePosted(_channel, message, false, null), Token);
		await DrainAsync();

		Assert.Empty(Posts());
	}

	#endregion

	#region Long answers

	[Fact]
	public async Task AReplyWithSeveralLines_IsPostedAsOneMessagePerLine()
	{
		GivenFaq("rules", "first\nsecond\nthird");

		await PostAsync(_channel, "!faq rules");

		Assert.Equal(["first", "second", "third"], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task AReplyWithEmptyLines_SkipsThem()
	{
		GivenFaq("rules", "first\n\n\nsecond\n");

		await PostAsync(_channel, "!faq rules");

		Assert.Equal(["first", "second"], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task AReplyWithWindowsLineEndings_IsPostedWithoutCarriageReturns()
	{
		GivenFaq("rules", "first\r\nsecond");

		await PostAsync(_channel, "!faq rules");

		Assert.Equal(["first", "second"], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task ALineOfExactlyTheLimit_IsPostedWhole()
	{
		var line = new string('a', MaxPieceLength);
		GivenFaq("long", line);

		await PostAsync(_channel, "!faq long");

		Assert.Equal([line], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task ALineOverTheLimit_IsSplitAtTheLastSpaceBeforeTheLimit()
	{
		var head = new string('A', 1000) + " " + new string('B', 900);
		var tail = new string('C', 500);
		GivenFaq("long", head + " " + tail);

		await PostAsync(_channel, "!faq long");

		Assert.Equal([head, tail], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task ALineWhoseFirstPartEndsExactlyAtTheLimit_IsSplitAtThatSpace()
	{
		var head = new string('a', MaxPieceLength);
		var tail = new string('b', 10);
		GivenFaq("long", head + " " + tail);

		await PostAsync(_channel, "!faq long");

		Assert.Equal([head, tail], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task ALineOverTheLimitWithoutSpaces_IsSplitEveryLimitCharacters()
	{
		GivenFaq("long", new string('x', 4500));

		await PostAsync(_channel, "!faq long");

		Assert.Equal([new string('x', 2000), new string('x', 2000), new string('x', 500)], _bot.SaidIn(_channel));
	}

	[Fact]
	public async Task EveryPieceOfALongLine_IsAtMostTheLimit()
	{
		GivenFaq("long", string.Join(' ', Enumerable.Repeat("word", 1500)));

		await PostAsync(_channel, "!faq long");

		var pieces = _bot.SaidIn(_channel);
		Assert.True(pieces.Count > 1);
		Assert.All(pieces, piece => Assert.True(piece.Length <= MaxPieceLength));
	}

	#endregion

	#region Failures

	[Fact]
	public async Task ACommandThatThrows_SaysNothing()
	{
		GivenFailingFaq("boom");

		await PostAsync(_channel, "!faq boom");

		Assert.Empty(Posts());
	}

	[Fact]
	public async Task AMessageAfterACommandThatThrew_IsStillAnswered()
	{
		GivenFailingFaq("boom");
		await PostAsync(_channel, "!faq boom");

		await PostAsync(_channel, "!roll");

		Assert.Equal([_bot.Text(BotReplies.RollResult, new { user = "Typist", points = 0L })], _bot.SaidIn(_channel));
	}

	#endregion
}