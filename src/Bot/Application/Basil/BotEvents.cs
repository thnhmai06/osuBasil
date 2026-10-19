using Basil.Domain.Bancho;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>Something that happened on the Basil server that the bot reacts to.</summary>
public abstract record BotEvent;

/// <summary>A chat message the bot can see arrived.</summary>
/// <param name="Message">The message, with its author.</param>
/// <param name="Channel">The channel it was posted in, or <see langword="null" /> when it is a private message to the bot.</param>
public sealed record MessageReceived(Message Message, string? Channel) : BotEvent;

/// <summary>A round started in a room.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="PlayerCount">How many players play it.</param>
public sealed record RoomRoundStarted(int RoomId, int PlayerCount) : BotEvent;

/// <summary>The round in progress in a room was aborted.</summary>
/// <param name="RoomId">The room.</param>
public sealed record RoomRoundAborted(int RoomId) : BotEvent;

/// <summary>A room's settings changed.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="RoundSettingsChanged">
///     Whether a setting the round is played with changed (beatmap, mode, mods, freemods,
///     team type or win condition).
/// </param>
public sealed record RoomSettingsChanged(int RoomId, bool RoundSettingsChanged) : BotEvent;

/// <summary>An empty room announced when it will close if nobody joins.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="ClosesAt">When it closes.</param>
public sealed record RoomClosingAnnounced(int RoomId, DateTimeOffset ClosesAt) : BotEvent;

/// <summary>A player took a seat in a room.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="Player">The player.</param>
public sealed record RoomPlayerJoined(int RoomId, UserRecord Player) : BotEvent;

/// <summary>A room closed.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="By">The user who closed it, or <see langword="null" /> when it closed because it stayed empty.</param>
public sealed record RoomClosed(int RoomId, UserRecord? By) : BotEvent;

/// <summary>A player's client reported signs of cheating.</summary>
/// <param name="Player">The player.</param>
/// <param name="Signs">The cheat signs reported.</param>
/// <param name="RoomId">The room the player sits in, or <see langword="null" /> when none.</param>
public sealed record PlayerFlagged(UserRecord Player, BanchoFlags Signs, int? RoomId) : BotEvent;