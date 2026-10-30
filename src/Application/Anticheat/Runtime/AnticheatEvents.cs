using Basil.Application.Users.Connections;
using Basil.Application.Common.Events;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Runtime;

/// <summary>Something the anticheat noticed.</summary>
public abstract record AnticheatEvent : Event;

/// <summary>A player's osu! client reported signs of cheating.</summary>
/// <param name="Player">The player's osu! client.</param>
/// <param name="Signs">The reported flags that are signs of cheating; never <see cref="OsuFlags.Clean" />.</param>
public sealed record AnticheatPlayerFlagged(BanchoConnection Player, OsuFlags Signs) : AnticheatEvent;