using System.Collections.Concurrent;
using Bancho.Domain;

namespace Bancho.Application.Sessions;

/// <summary>
/// Ported from app/objects/channel.py's Channel — DB metadata + live in-memory membership.
/// `Name` is the registry key (Python's `real_name`); `DisplayName` is what's actually sent to
/// clients in packets — for ordinary channels these are the same, but instance channels (e.g.
/// `#spec_123`) always display as a fixed name (`#spectator`) regardless of which instance a
/// given client is currently in, since multiple instances exist concurrently server-side.
/// </summary>
public sealed class ChannelSession(
    int id, string name, string topic, int readPriv, int writePriv, bool autoJoin,
    string? displayName = null, bool instance = false)
{
    private readonly ConcurrentDictionary<int, byte> _members = new();

    public int Id { get; } = id;
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName ?? name;
    public string Topic { get; } = topic;
    public int ReadPriv { get; } = readPriv;
    public int WritePriv { get; } = writePriv;
    public bool AutoJoin { get; } = autoJoin;
    public bool Instance { get; } = instance;

    public int PlayerCount => _members.Count;

    public IReadOnlyCollection<int> MemberIds => _members.Keys.ToArray();

    public bool CanRead(Privileges priv) => ReadPriv == 0 || ((int)priv & ReadPriv) != 0;

    public bool CanWrite(Privileges priv) => WritePriv == 0 || ((int)priv & WritePriv) != 0;

    public void Join(int playerId) => _members[playerId] = 0;

    public void Part(int playerId) => _members.TryRemove(playerId, out _);

    public bool Contains(int playerId) => _members.ContainsKey(playerId);
}
