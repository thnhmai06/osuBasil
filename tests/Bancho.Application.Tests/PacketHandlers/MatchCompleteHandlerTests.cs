using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's MatchComplete, including the is_scrimming -> scrim-scoring branch.</summary>
public class MatchCompleteHandlerTests
{
    [Fact]
    public async Task Handle_OtherPlayerStillPlaying_DoesNotFinishMatch()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.InProgress = true;
        host.Dequeue();
        guest.Dequeue();
        var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.MakeScoringService());

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.Equal(SlotStatus.Complete, match.Slots[0].Status);
        Assert.True(match.InProgress);
        Assert.Empty(host.Dequeue());
    }

    [Fact]
    public async Task Handle_EveryoneDone_FinishesMatchAndBroadcastsCompleteImmuneToNonPlayers()
    {
        var fixture = new Fixture();
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        var spectatorish = MakePlayer(3, "afk"); // in the room but never played (status stays Open/NotReady)
        fixture.RegisterAll(host, guest, spectatorish);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        fixture.MatchMembership.Join(spectatorish, match, "");
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        match.Slots[2].Status = SlotStatus.NotReady; // never started playing
        match.InProgress = true;
        match.Slots[1].Loaded = true;
        host.Dequeue();
        guest.Dequeue();
        spectatorish.Dequeue();
        var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.MakeScoringService());

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        await handler.HandleAsync(guest, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));

        Assert.False(match.InProgress);
        Assert.False(match.Slots[1].Loaded);
        Assert.Contains(ServerPacketWriter.MatchComplete(), Chunk(host.Dequeue()));
        // immune from match_complete itself (still gets the enqueue_state update, just not this packet)
        Assert.DoesNotContain(ServerPacketWriter.MatchComplete(), Chunk(spectatorish.Dequeue()));
    }

    [Fact]
    public async Task Handle_ScrimMatchEveryoneDone_LaunchesScoringAfterReleasingTheLock()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('a', 32);
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        fixture.MatchMembership.Join(guest, match, "");
        match.IsScrimming = true;
        match.MapMd5 = mapMd5;
        match.Slots[0].Status = SlotStatus.Playing;
        match.Slots[1].Status = SlotStatus.Playing;
        host.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 500_000, 98.0, 400);
        guest.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 300_000, 95.0, 200);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(new Beatmap(
            Md5: mapMd5, Id: 1, SetId: 1, Artist: "A", Title: "T", Version: "V", Creator: "C",
            LastUpdate: DateTime.UtcNow, TotalLength: 60, MaxCombo: 100, Status: RankedStatus.Ranked,
            Frozen: false, Plays: 0, Passes: 0, Mode: GameMode.VanillaOsu, Bpm: 120, Cs: 4, Od: 8, Ar: 9,
            Hp: 5, Diff: 5.0, Filename: "map.osu"));
        var handler = new MatchCompleteHandler(fixture.MatchMembership, fixture.MakeScoringService());

        await handler.HandleAsync(host, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        await handler.HandleAsync(guest, new BanchoPacketReader(ReadOnlyMemory<byte>.Empty));
        await Task.Delay(200); // let the fire-and-forget scoring task (tiny injected poll budget) finish

        Assert.Single(match.Winners);
        Assert.Equal(host.Id, match.Winners[0]!.Value.PlayerId);
    }
}
