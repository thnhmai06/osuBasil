using Microsoft.Extensions.Options;
using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;
using OpenOsuTournament.Bancho.Application.UseCases.Bot;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Bot;

public class CommandDispatcherTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();

    private CommandDispatcher MakeDispatcher(string prefix = "!")
    {
        var options = Options.Create(new ServerBehaviorOptions
        {
            Domain = "test.local",
            CommandPrefix = prefix,
            MenuIconUrl = "https://example.test/icon.png",
            MenuOnclickUrl = "https://example.test"
        });
        var fixture = new MultiplayerTestSupport.Fixture();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var mpCommands = new MpCommandService(fixture.MatchMembership, fixture.MatchPersistence, _maps,
            fixture.SessionRegistry, clock);
        return new CommandDispatcher(options, mpCommands);
    }

    [Fact]
    public async Task DispatchAsync_NoPrefix_ReturnsNull()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "hello there", null);

        Assert.Null(reply);
    }

    [Fact]
    public async Task DispatchAsync_UnknownCommand_ReturnsNull()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "!bogus", null);

        Assert.Null(reply);
    }

    [Fact]
    public async Task DispatchAsync_Help_ReturnsHelpText()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "!help", null);

        Assert.NotNull(reply);
    }

    [Fact]
    public async Task DispatchAsync_RollNoArg_DefaultsMaxTo100()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "!roll", null);

        Assert.NotNull(reply);
        Assert.StartsWith("cmyui rolls ", reply);
        var pointsToken = reply!.Split(' ')[2];
        var points = int.Parse(pointsToken);
        Assert.InRange(points, 0, 100);
    }

    [Fact]
    public async Task DispatchAsync_RollArgAboveCap_ClampsTo32767()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "!roll 999999", null);

        var points = int.Parse(reply!.Split(' ')[2]);
        Assert.InRange(points, 0, 0x7FFF);
    }

    [Fact]
    public async Task DispatchAsync_MpWithoutMatchScope_ReturnsNull()
    {
        var dispatcher = MakeDispatcher();
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        var reply = await dispatcher.DispatchAsync(sender, "!mp settings", null);

        Assert.Null(reply);
    }

    [Fact]
    public async Task DispatchAsync_MpWithMatchScope_RoutesToMpCommandService()
    {
        var dispatcher = MakeDispatcher();
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var fixture = new MultiplayerTestSupport.Fixture();
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);

        var reply = await dispatcher.DispatchAsync(host, "!mp help", match);

        Assert.NotNull(reply);
        Assert.Contains("settings", reply);
    }

    [Fact]
    public async Task DispatchAsync_CustomPrefix_IsRespected()
    {
        var dispatcher = MakeDispatcher(".");
        var sender = MultiplayerTestSupport.MakePlayer(1, "cmyui");

        Assert.Null(await dispatcher.DispatchAsync(sender, "!roll", null));
        Assert.NotNull(await dispatcher.DispatchAsync(sender, ".roll", null));
    }
}
