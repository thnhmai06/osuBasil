using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Bot.Commands;
using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Bot.Registry;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Chat;
using Basil.Infrastructure.Bot.Handlers.Commands;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;
using Basil.Infrastructure.Bot.Services;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Bot.Tests.Support;

public sealed class Said
{
	public List<string> Here { get; } = [];

	public List<string> Privately { get; } = [];

	public IEnumerable<string> All => Here.Concat(Privately);
}

internal sealed class BotHarness : IDisposable
{
	public BotHarness(Random? random = null)
	{
		App = new TestApplication();
		Replies = new ReplyCatalog(ReplyDefinitions.In(typeof(Reply).Assembly),
			Options.Create(new RepliesOptions { Path = Path.Combine(Path.GetTempPath(), "basil-bot-tests-none.json") }),
			NullLogger<ReplyCatalog>.Instance);
		Users = App.Provider.GetRequiredService<IUserRepository>();
		Beatmaps = App.Provider.GetRequiredService<IBeatmapRepository>();
		Faqs = App.Provider.GetRequiredService<IFaqStorage>();
		Targets = new MpTargets();
		Mp = new MpCommands(App.Lobby, App.Users, Users, Beatmaps, Targets, App.Sender);
		Chat = new ChatCommands(Users, Faqs, Mp, new ChannelCommands(App.Sender, App.Channels),
			random ?? new FixedRandom(0));
	}

	public TestApplication App { get; }

	public ReplyCatalog Replies { get; }

	public IUserRepository Users { get; }

	public IBeatmapRepository Beatmaps { get; }

	public IFaqStorage Faqs { get; }

	public MpTargets Targets { get; }

	public MpCommands Mp { get; }

	public ChatCommands Chat { get; }

	public void Dispose()
	{
		App.Dispose();
	}

	public async Task<User> StartBotAsync()
	{
		await App.SeedBotAsync();
		var bot = await App.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		Assert.NotNull(bot);
		return bot;
	}

	public Task<GeneralChannel> OpenChannelAsync(string name)
	{
		var channel = App.Server.OpenChannel(new GeneralChannelRecord
			{ Name = name, Value = new GeneralChannelData() });
		Assert.NotNull(channel);
		return Task.FromResult(channel);
	}

	public List<string> SaidIn(Channel channel)
	{
		return
		[
			.. App.ChannelEvents.Events.OfType<ChannelMessagePosted>()
				.Where(posted => ReferenceEquals(posted.Channel, channel))
				.Select(posted => posted.Message.Content)
		];
	}

	public List<string> WhisperedTo(Connection connection)
	{
		return SaidIn(App.PmChannels.Of(connection.User));
	}

	public async Task<(bool Succeeded, Said Said)> TypeAsync(Connection actor, string? channel, string text)
	{
		var said = new Said();
		var context = new CommandContext(actor, channel, Replies,
			(line, _) =>
			{
				said.Here.Add(line);
				return Task.CompletedTask;
			},
			(line, _) =>
			{
				said.Privately.Add(line);
				return Task.CompletedTask;
			});
		var succeeded = await Chat.ExecuteAsync(context, text, TestContext.Current.CancellationToken);
		return (succeeded, said);
	}

	public string Text(Reply reply, object? arguments = null)
	{
		return Replies.Text(reply, arguments);
	}

	public string TextIn(int room, Reply reply, object? arguments = null)
	{
		return Prefixed(room, Text(reply, arguments));
	}

	public string Prefixed(int room, string text)
	{
		var prefix = Text(MpReplies.RoomPrefix, new { room });
		return string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
	}

	public async Task<Room> MakeAsync(Connection actor, string name, bool isPrivate = false)
	{
		var (succeeded, _) = await TypeAsync(actor, "#osu", $"!mp {(isPrivate ? "makeprivate" : "make")} {name}");
		Assert.True(succeeded);
		return App.Lobby.Rooms.Values.Single(room => room.Settings.Name == name);
	}

	private sealed class FixedRandom(long value) : Random
	{
		public override long NextInt64(long minValue, long maxValue)
		{
			return Math.Clamp(value, minValue, maxValue - 1);
		}
	}
}