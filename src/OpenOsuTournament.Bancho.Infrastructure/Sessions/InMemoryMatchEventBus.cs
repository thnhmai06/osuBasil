using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;

namespace OpenOsuTournament.Bancho.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchEventBus" />
public sealed class InMemoryMatchEventBus : IMatchEventBus
{
    private readonly ConcurrentDictionary<int, Hub> _hubs = new();

    public IDisposable SubscribeMain(int matchDbId, ChannelWriter<byte[]> writer)
    {
        var hub = GetHub(matchDbId);
        hub.Main[writer] = 0;
        return new Subscription(() => hub.Main.TryRemove(writer, out _));
    }

    public IDisposable SubscribePlayer(int matchDbId, string playerName, ChannelWriter<byte[]> writer)
    {
        var hub = GetHub(matchDbId);
        var subs = hub.Players.GetOrAdd(playerName, _ => new ConcurrentDictionary<ChannelWriter<byte[]>, byte>());
        subs[writer] = 0;
        return new Subscription(() => subs.TryRemove(writer, out _));
    }

    public IDisposable SubscribeInput(int matchDbId, ChannelWriter<byte[]> writer)
    {
        var hub = GetHub(matchDbId);
        hub.Input[writer] = 0;
        return new Subscription(() => hub.Input.TryRemove(writer, out _));
    }

    public void PublishMain(int matchDbId, byte[] payload)
    {
        if (_hubs.TryGetValue(matchDbId, out var hub))
            foreach (var writer in hub.Main.Keys)
                writer.TryWrite(payload);
    }

    public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
    {
        if (_hubs.TryGetValue(matchDbId, out var hub) && hub.Players.TryGetValue(playerName, out var subs))
            foreach (var writer in subs.Keys)
                writer.TryWrite(payload);
    }

    public void PublishInput(int matchDbId, byte[] payload)
    {
        if (_hubs.TryGetValue(matchDbId, out var hub))
            foreach (var writer in hub.Input.Keys)
                writer.TryWrite(payload);
    }

    private Hub GetHub(int matchDbId)
    {
        return _hubs.GetOrAdd(matchDbId, _ => new Hub());
    }

    private sealed class Hub
    {
        public readonly ConcurrentDictionary<ChannelWriter<byte[]>, byte> Main = new();
        public readonly ConcurrentDictionary<ChannelWriter<byte[]>, byte> Input = new();
        public readonly ConcurrentDictionary<string, ConcurrentDictionary<ChannelWriter<byte[]>, byte>> Players = new();
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
        }
    }
}
