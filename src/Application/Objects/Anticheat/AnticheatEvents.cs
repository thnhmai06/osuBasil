using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Objects.Anticheat;

/// <summary>Something the anticheat noticed.</summary>
public abstract record AnticheatEvent : Event;

/// <summary>A player's osu! client reported signs of cheating.</summary>
/// <param name="Player">The player's osu! client.</param>
/// <param name="Signs">The reported flags that are signs of cheating; never <see cref="BanchoFlags.Clean" />.</param>
/// <param name="Room">The room the player sat in when the flags were reported, or <see langword="null" /> when none.</param>
public sealed record AnticheatPlayerFlagged(BanchoConnection Player, BanchoFlags Signs, Room? Room) : AnticheatEvent;