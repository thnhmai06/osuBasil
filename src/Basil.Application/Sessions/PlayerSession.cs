using System.Collections.Concurrent;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     Server-side representation of an online player. Ported from app/objects/player.py's Player.
///     Holds runtime session state: packet queue, channel memberships, spectator list, match
///     reference, per-mode stats, geolocation, and IRC transport bridge.
/// </summary>
public sealed class PlayerSession(int id, string name, string token, UserPrivileges priv, DateTimeOffset loginTime)
{
    private readonly ConcurrentDictionary<string, byte> _channels = new();
    private readonly ConcurrentQueue<byte[]> _packetQueue = new();
    private readonly ConcurrentDictionary<int, PlayerSession> _spectators = new();

    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Token { get; } = token;
    public UserPrivileges Priv { get; set; } = priv;
    public DateTimeOffset LoginTime { get; } = loginTime;
    public DateTimeOffset LastRecvTime { get; set; } = loginTime;

    public int UtcOffset { get; init; }

    /// <summary>
    ///     True only for the bootstrapped BanchoBot session. Never sends real ping packets, so
    ///     <see cref="LastRecvTime" /> never advances — exempted from GhostDisconnectService's reap
    ///     sweep for exactly that reason.
    /// </summary>
    public bool IsBot { get; init; }

    /// <summary>Set at login from the client's login body, but mutable at runtime via TOGGLE_BLOCK_NON_FRIEND_DMS.</summary>
    public bool PmPrivate { get; set; }

    public DateTimeOffset SilenceEnd { get; set; } = DateTimeOffset.UnixEpoch;
    public string? AwayMessage { get; set; }
    public PresenceFilter PresenceFilter { get; set; } = PresenceFilter.Nil;

    /// <summary>
    ///     Set by `!mp in &lt;match_id&gt;` — the <see cref="Sessions.Multiplayer.MatchSession.DbId" /> of
    ///     the match this referee is currently targeting from outside that match's own chat channel.
    ///     Overrides the channel-derived match scope for every `!mp` subcommand until changed by another
    ///     `!mp in` or cleared implicitly by `!mp make`/`makeprivate` re-pointing it at the new room.
    /// </summary>
    public int? MpScopeMatchId { get; set; }

    /// <summary>Ported from Player.geoloc — defaults to "xx"/0/0 when unavailable, matching Player.__init__.</summary>
    public Geolocation Geoloc { get; set; } = new(0.0, 0.0, Country.Xx);

    /// <summary>
    ///     Ported from Player.client_details — the hardware/version fingerprint captured at login,
    ///     re-checked against score submission's own client_hash to catch a submission from a
    ///     different client session than the one currently logged in.
    /// </summary>
    public ClientDetails? Client { get; set; }

    /// <summary>
    ///     The osu! client version captured at login (alongside <see cref="Client" />) — kept separate
    ///     since <see cref="ClientDetails" /> no longer carries a version date (it isn't part of the
    ///     client-hash string). Score submission's version-mismatch check compares against this.
    /// </summary>
    public OsuVersion? OsuVersion { get; set; }

    /// <summary>Ported from Player.spectating — the session this player is currently spectating, if any.</summary>
    public PlayerSession? Spectating { get; set; }

    /// <summary>Ported from Player.match — the multiplayer match this player is currently in, if any.</summary>
    public MatchSession? Match { get; set; }

    /// <summary>
    ///     Ported from Player.stealth — an admin spectating without the target being informed.
    ///     Toggled by the `!stealth` command; defaults off.
    /// </summary>
    public bool Stealth { get; set; }

    /// <summary>Ported from Player.spectators — the sessions currently spectating this player.</summary>
    public IReadOnlyCollection<PlayerSession> Spectators => _spectators.Values.ToArray();

    public PlayerStatus Status { get; } = new();

    /// <summary>
    ///     Per-mode stats, cached in memory at login (stats_from_sql_full) and never re-queried per
    ///     packet. Ported from Player.stats (dict[GameMode, ModeData]).
    /// </summary>
    public Dictionary<GameMode, CachedPlayerStats> ModeStats { get; } = new();

    /// <summary>Ported from Player.gm_stats — the cached stats for the player's currently selected mode.</summary>
    public CachedPlayerStats? CurrentStats => ModeStats.GetValueOrDefault(Status.Mode);

    public string SafeName => User.MakeSafeName(Name);

    public bool Restricted => (Priv & UserPrivileges.Unrestricted) == 0;

    /// <summary>Ported from Player.bancho_priv — maps server-side privileges to client-facing ones.</summary>
    public ClientPrivileges BanchoPriv
    {
        get
        {
            var result = (ClientPrivileges)0;
            if ((Priv & UserPrivileges.Unrestricted) != 0) result |= ClientPrivileges.Player;

            if ((Priv & UserPrivileges.Donator) != 0) result |= ClientPrivileges.Supporter;

            if ((Priv & UserPrivileges.Moderator) != 0) result |= ClientPrivileges.Moderator;

            if ((Priv & UserPrivileges.Administrator) != 0) result |= ClientPrivileges.Developer;

            if ((Priv & UserPrivileges.Developer) != 0) result |= ClientPrivileges.Owner;

            return result;
        }
    }

    public TimeSpan RemainingSilence => SilenceEnd > DateTimeOffset.UtcNow ? SilenceEnd - DateTimeOffset.UtcNow : TimeSpan.Zero;

    public bool Silenced => RemainingSilence != TimeSpan.Zero;

    /// <summary>Ported from Player.channels — the set of channel names this session has joined.</summary>
    public IReadOnlyCollection<string> Channels => _channels.Keys.ToArray();

    /// <summary>
    ///     The IRC-shaped transport chat is routed through for this session — a bancho packet bridge by
    ///     default, replaced with a real <c>TcpIrcConnection</c> for a session created by an actual IRC
    ///     login. Lazily defaults to a bridge wrapping this session, so any <see cref="PlayerSession" />
    ///     works out of the box even if the constructing code never wires one explicitly (tests, mostly)
    ///     — a plain auto-property can't self-reference `this` in its initializer, hence the backing field.
    /// </summary>
    public IIrcConnection IrcConnection
    {
        get => field ??= new BanchoIrcBridgeConnection(this);
        init;
    }

    public void AddSpectator(PlayerSession spectator)
    {
        _spectators[spectator.Id] = spectator;
    }

    public void RemoveSpectator(PlayerSession spectator)
    {
        _spectators.TryRemove(spectator.Id, out _);
    }

    public void Enqueue(byte[] data)
    {
        _packetQueue.Enqueue(data);
    }

    public void JoinChannel(string name)
    {
        _channels[name] = 0;
    }

    public void LeaveChannel(string name)
    {
        _channels.TryRemove(name, out _);
    }

    public bool InChannel(string name)
    {
        return _channels.ContainsKey(name);
    }

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
    public UserActivity UserActivity { get; set; } = UserActivity.Idle;
    public string InfoText { get; set; } = "";
    public string MapMd5 { get; set; } = "";
    public Mods Mods { get; set; } = Mods.NoMod;
    public GameMode Mode { get; set; } = GameMode.Standard;
    public int MapId { get; set; }
}

/// <summary>Ported from app/objects/player.py's ModeData — a player's cached stats in a single gamemode.</summary>
public sealed record CachedPlayerStats(long Tscore, long Rscore, double Acc, int Plays, int Rank);