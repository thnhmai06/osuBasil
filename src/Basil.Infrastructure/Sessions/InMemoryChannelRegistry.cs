using System.Collections.Concurrent;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Sessions.Channels;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IChannelRegistry" />
public sealed class InMemoryChannelRegistry : IChannelRegistry
{
    private readonly ConcurrentDictionary<string, ChannelSession> _byName = new();

    public void Seed(IReadOnlyList<Channel> channels)
    {
        foreach (var channel in channels)
            _byName[channel.Name] = new ChannelSession(
                channel.Id, channel.Name, channel.Topic, channel.ReadPriv, channel.WritePriv, channel.AutoJoin);
    }

    public void Add(ChannelSession channel)
    {
        _byName[channel.Name] = channel;
    }

    public void Remove(string name)
    {
        _byName.TryRemove(name, out _);
    }

    public ChannelSession? GetByName(string name)
    {
        return _byName.GetValueOrDefault(name);
    }

    public IReadOnlyList<ChannelSession> AutoJoinChannels =>
        _byName.Values.Where(c => c.AutoJoin).ToList();

    public IReadOnlyList<ChannelSession> All => _byName.Values.ToList();
}