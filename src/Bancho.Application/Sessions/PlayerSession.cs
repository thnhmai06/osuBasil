using System.Collections.Concurrent;
using Bancho.Domain;

namespace Bancho.Application.Sessions;

/// <summary>
/// Server-side representation of an online player. Ported from app/objects/player.py's Player,
/// scoped to what Phase 3 (login + basic packet handlers) needs — richer state (match,
/// spectating, clan, friends/blocks as loaded sets) is added when the phase that consumes it
/// lands, matching the fields' actual introduction order in bancho.py's own history.
/// </summary>
public sealed class PlayerSession(int id, string name, string token, Privileges priv, double loginTime, bool isBotClient = false)
{
    private readonly ConcurrentQueue<byte[]> _packetQueue = new();
    private readonly ConcurrentDictionary<string, byte> _channels = new();

    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Token { get; } = token;
    public Privileges Priv { get; set; } = priv;
    public double LoginTime { get; } = loginTime;
    public double LastRecvTime { get; set; } = loginTime;

    /// <summary>Ported from Player.is_bot_client — a bot session never accumulates outgoing packets.</summary>
    public bool IsBotClient { get; } = isBotClient;

    public int UtcOffset { get; init; }

    /// <summary>Set at login from the client's login body, but mutable at runtime via TOGGLE_BLOCK_NON_FRIEND_DMS.</summary>
    public bool PmPrivate { get; set; }
    public long SilenceEnd { get; set; }
    public long DonorEnd { get; set; }
    public string? AwayMessage { get; set; }
    public PresenceFilter PresenceFilter { get; set; } = PresenceFilter.Nil;

    /// <summary>Ported from Player.geoloc — defaults to "xx"/0/0 when unavailable, matching Player.__init__.</summary>
    public Geolocation Geoloc { get; set; } = new(0.0, 0.0, "xx", 0);

    public PlayerStatus Status { get; } = new();

    /// <summary>
    /// Per-mode stats, cached in memory at login (stats_from_sql_full) and never re-queried per
    /// packet. Ported from Player.stats (dict[GameMode, ModeData]).
    /// </summary>
    public Dictionary<int, CachedPlayerStats> ModeStats { get; } = new();

    /// <summary>Ported from Player.gm_stats — the cached stats for the player's currently selected mode.</summary>
    public CachedPlayerStats? CurrentStats => ModeStats.GetValueOrDefault((int)Status.Mode);

    public string SafeName => Domain.SafeName.Make(Name);

    public bool Restricted => (Priv & Privileges.Unrestricted) == 0;

    /// <summary>Ported from Player.bancho_priv — maps server-side privileges to client-facing ones.</summary>
    public ClientPrivileges BanchoPriv
    {
        get
        {
            var result = (ClientPrivileges)0;
            if ((Priv & Privileges.Unrestricted) != 0)
            {
                result |= ClientPrivileges.Player;
            }

            if ((Priv & Privileges.Donator) != 0)
            {
                result |= ClientPrivileges.Supporter;
            }

            if ((Priv & Privileges.Moderator) != 0)
            {
                result |= ClientPrivileges.Moderator;
            }

            if ((Priv & Privileges.Administrator) != 0)
            {
                result |= ClientPrivileges.Developer;
            }

            if ((Priv & Privileges.Developer) != 0)
            {
                result |= ClientPrivileges.Owner;
            }

            return result;
        }
    }

    public long RemainingSilence => Math.Max(0, SilenceEnd - DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public bool Silenced => RemainingSilence != 0;

    public void Enqueue(byte[] data)
    {
        if (!IsBotClient)
        {
            _packetQueue.Enqueue(data);
        }
    }

    /// <summary>Ported from Player.channels — the set of channel names this session has joined.</summary>
    public IReadOnlyCollection<string> Channels => _channels.Keys.ToArray();

    public void JoinChannel(string name) => _channels[name] = 0;

    public void LeaveChannel(string name) => _channels.TryRemove(name, out _);

    public bool InChannel(string name) => _channels.ContainsKey(name);

    /// <summary>Drains and concatenates all queued outgoing packet bytes, clearing the queue.</summary>
    public byte[] Dequeue()
    {
        var chunks = new List<byte[]>();
        var totalLength = 0;

        while (_packetQueue.TryDequeue(out var chunk))
        {
            chunks.Add(chunk);
            totalLength += chunk.Length;
        }

        var result = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }

        return result;
    }
}

/// <summary>Ported from app/objects/player.py's Status — the client's currently reported state.</summary>
public sealed class PlayerStatus
{
    public Domain.Action Action { get; set; } = Domain.Action.Idle;
    public string InfoText { get; set; } = "";
    public string MapMd5 { get; set; } = "";
    public Mods Mods { get; set; } = Mods.NoMod;
    public GameMode Mode { get; set; } = GameMode.VanillaOsu;
    public int MapId { get; set; }
}

/// <summary>Ported from app/objects/player.py's ModeData — a player's cached stats in a single gamemode.</summary>
public sealed record CachedPlayerStats(long Tscore, long Rscore, double Acc, int Plays, int Playtime, int MaxCombo, int TotalHits, int Rank);
