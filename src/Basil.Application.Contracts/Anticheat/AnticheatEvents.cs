using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;

namespace Basil.Application.Contracts.Anticheat;

/// <summary>Something the anticheat noticed.</summary>
public abstract record AnticheatEvent : Event;

/// <summary>A player's osu! client reported signs of cheating.</summary>
/// <param name="Player">The player's osu! client.</param>
/// <param name="Signs">The reported flags that are signs of cheating; never <see cref="ClientFlags.Clean" />.</param>
/// <param name="Room">The room the player sat in when the flags were reported, or <see langword="null" /> when none.</param>
public sealed record AnticheatPlayerFlagged(BanchoConnection Player, ClientFlags Signs, Room? Room) : AnticheatEvent;