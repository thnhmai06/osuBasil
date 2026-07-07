using Bancho.Application.Abstractions;
using Bancho.Application.Commands;
using Bancho.Application.Configuration;
using Bancho.Domain;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.Commands;

/// <summary>Ported from app/commands.py's mp_map.</summary>
public class MpMapCommandTests
{
    private static readonly IOptions<ServerBehaviorOptions> ServerOptions = Options.Create(new ServerBehaviorOptions
    {
        Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://x", MenuOnclickUrl = "https://x",
    });

    private static Beatmap MakeBeatmap(int id, string md5) => new(
        Md5: md5, Id: id, SetId: 1, Artist: "A", Title: "T", Version: "V", Creator: "C",
        LastUpdate: DateTime.UtcNow, TotalLength: 60, MaxCombo: 100, Status: RankedStatus.Ranked,
        Frozen: false, Plays: 0, Passes: 0, Mode: GameMode.RelaxOsu, Bpm: 120, Cs: 4, Od: 8, Ar: 9,
        Hp: 5, Diff: 5.0, Filename: "map.osu");

    [Fact]
    public async Task HandleAsync_InvalidSyntax_ReturnsUsage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpMapCommand(fixture.MapRepository, fixture.MatchMembership, ServerOptions);

        var response = await command.HandleAsync(new MpCommandContext(host, ["notanumber"], match));

        Assert.Equal("Invalid syntax: !mp map <beatmapid>", response);
    }

    [Fact]
    public async Task HandleAsync_MapAlreadySelected_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var command = new MpMapCommand(fixture.MapRepository, fixture.MatchMembership, ServerOptions);

        var response = await command.HandleAsync(new MpCommandContext(host, [match.MapId.ToString()], match));

        Assert.Equal("Map already selected.", response);
    }

    [Fact]
    public async Task HandleAsync_BeatmapNotFound_ReturnsMessage()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        fixture.MapRepository.FetchOneAsync(id: 999).Returns((Beatmap?)null);
        var command = new MpMapCommand(fixture.MapRepository, fixture.MatchMembership, ServerOptions);

        var response = await command.HandleAsync(new MpCommandContext(host, ["999"], match));

        Assert.Equal("Beatmap not found.", response);
    }

    [Fact]
    public async Task HandleAsync_Found_UpdatesMatchAndReturnsEmbed()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        var newMd5 = new string('b', 32);
        var bmap = MakeBeatmap(999, newMd5);
        fixture.MapRepository.FetchOneAsync(id: 999).Returns(bmap);
        var command = new MpMapCommand(fixture.MapRepository, fixture.MatchMembership, ServerOptions);

        var response = await command.HandleAsync(new MpCommandContext(host, ["999"], match));

        Assert.Equal(999, match.MapId);
        Assert.Equal(newMd5, match.MapMd5);
        Assert.Equal(bmap.FullName, match.MapName);
        Assert.Equal(GameMode.RelaxOsu, match.Mode);
        Assert.Contains("Selected:", response);
        Assert.Contains(bmap.FullName, response);
    }
}
