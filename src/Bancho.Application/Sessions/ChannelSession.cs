using System.Collections.Concurrent;
using Bancho.Domain;

namespace Bancho.Application.Sessions;

/// <summary>Ported from app/objects/channel.py's Channel — DB metadata + live in-memory membership.</summary>
public sealed class ChannelSession(int id, string name, string topic, int readPriv, int writePriv, bool autoJoin)
{
    private readonly ConcurrentDictionary<int, byte> _members = new();

    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Topic { get; } = topic;
    public int ReadPriv { get; } = readPriv;
    public int WritePriv { get; } = writePriv;
    public bool AutoJoin { get; } = autoJoin;

    public int PlayerCount => _members.Count;

    public bool CanRead(Privileges priv) => ReadPriv == 0 || ((int)priv & ReadPriv) != 0;

    public bool CanWrite(Privileges priv) => WritePriv == 0 || ((int)priv & WritePriv) != 0;

    public void Join(int playerId) => _members[playerId] = 0;

    public void Part(int playerId) => _members.TryRemove(playerId, out _);

    public bool Contains(int playerId) => _members.ContainsKey(playerId);
}
