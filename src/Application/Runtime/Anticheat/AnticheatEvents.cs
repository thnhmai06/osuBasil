using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Events;
using Basil.Domain.Bancho;

namespace Basil.Application.Runtime.Anticheat;

/// <summary>Something the anticheat noticed.</summary>
public abstract record AnticheatEvent : Event;

/// <summary>A player's osu! client reported signs of cheating.</summary>
/// <param name="Player">The player's osu! client.</param>
/// <param name="Signs">The reported flags that are signs of cheating; never <see cref="BanchoFlags.Clean" />.</param>
public sealed record AnticheatPlayerFlagged(BanchoConnection Player, BanchoFlags Signs) : AnticheatEvent;