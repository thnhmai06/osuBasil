using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Runtime.Anticheat;

/// <summary>Something the anticheat noticed.</summary>
public abstract record AnticheatEvent : Event;

/// <summary>A player's osu! client reported signs of cheating.</summary>
/// <param name="Player">The player's osu! client.</param>
/// <param name="Signs">The reported flags that are signs of cheating; never <see cref="BanchoFlags.Clean" />.</param>
/// <param name="Room">The room the player sat in when the flags were reported, or <see langword="null" /> when none.</param>
public sealed record AnticheatPlayerFlagged(BanchoConnection Player, BanchoFlags Signs, Room? Room) : AnticheatEvent;