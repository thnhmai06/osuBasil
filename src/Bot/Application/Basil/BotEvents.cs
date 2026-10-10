using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>Something that happened on the Basil server that the bot reacts to.</summary>
public abstract record BotEvent;

/// <summary>A chat message the bot can see arrived.</summary>
/// <param name="Message">The message, with its author.</param>
/// <param name="Channel">The channel it was posted in, or <see langword="null" /> when it is a private message to the bot.</param>
public sealed record MessageReceived(Message Message, string? Channel) : BotEvent;

/// <summary>A countdown started in a room.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="Length">How long the countdown runs.</param>
/// <param name="StartsRound">Whether the round starts when it ends.</param>
public sealed record RoomCountdownStarted(int RoomId, TimeSpan Length, bool StartsRound) : BotEvent;

/// <summary>A running countdown reached one of its announced marks.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="Remaining">The time left.</param>
public sealed record RoomCountdownTicked(int RoomId, TimeSpan Remaining) : BotEvent;

/// <summary>A running countdown was cancelled by a user.</summary>
/// <param name="RoomId">The room.</param>
public sealed record RoomCountdownCancelled(int RoomId) : BotEvent;

/// <summary>A countdown ran out without starting a round.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="StartsRound">Whether it was meant to start the round (which could not start).</param>
public sealed record RoomCountdownElapsed(int RoomId, bool StartsRound) : BotEvent;

/// <summary>A round started in a room.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="PlayerCount">How many players play it.</param>
/// <param name="ByCountdown">Whether a countdown started it.</param>
public sealed record RoomRoundStarted(int RoomId, int PlayerCount, bool ByCountdown) : BotEvent;

/// <summary>The round in progress in a room was aborted.</summary>
/// <param name="RoomId">The room.</param>
public sealed record RoomRoundAborted(int RoomId) : BotEvent;

/// <summary>A room's settings changed.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="CountdownCancelled">Whether the change cancelled a countdown that would start the round.</param>
public sealed record RoomSettingsChanged(int RoomId, bool CountdownCancelled) : BotEvent;

/// <summary>An empty room announced when it will close if nobody joins.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="ClosesAt">When it closes.</param>
public sealed record RoomClosingAnnounced(int RoomId, DateTimeOffset ClosesAt) : BotEvent;

/// <summary>A player took a seat in a room.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="Player">The player.</param>
public sealed record RoomPlayerJoined(int RoomId, User Player) : BotEvent;

/// <summary>A room closed.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="By">The user who closed it, or <see langword="null" /> when it closed because it stayed empty.</param>
public sealed record RoomClosed(int RoomId, User? By) : BotEvent;

/// <summary>A player's client reported signs of cheating.</summary>
/// <param name="Player">The player.</param>
/// <param name="Signs">The cheat signs reported.</param>
/// <param name="RoomId">The room the player sits in, or <see langword="null" /> when none.</param>
public sealed record PlayerFlagged(User Player, ClientFlags Signs, int? RoomId) : BotEvent;
