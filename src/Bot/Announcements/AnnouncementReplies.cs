using Basil.Bot.Replies;

namespace Basil.Bot.Announcements;

/// <summary>
///     The user-visible reply text sent by BasilBot's room and anticheat announcements,
///     the single source of truth for that surface.
/// </summary>
internal static class AnnouncementReplies
{
	// ── Room countdown ─────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a countdown starts and will start the round; <c>{seconds}</c> is seconds.</summary>
	public static readonly Reply CountdownStartedForRound = new("announce.countdownStartedForRound",
		"Queued the match to start in {seconds} seconds", "seconds");

	/// <summary>Reply when a countdown starts but won't start the round; <c>{seconds}</c> is seconds.</summary>
	public static readonly Reply CountdownStarted = new("announce.countdownStarted",
		"Started a {seconds}-second countdown.", "seconds");

	/// <summary>Reply when a countdown ticks and will start the round; <c>{seconds}</c> is seconds remaining.</summary>
	public static readonly Reply CountdownTickedForRound = new("announce.countdownTickedForRound",
		"Match starts in {seconds} seconds", "seconds");

	/// <summary>Reply when a countdown ticks but won't start the round; <c>{seconds}</c> is seconds remaining.</summary>
	public static readonly Reply CountdownTicked =
		new("announce.countdownTicked", "{seconds} seconds remaining", "seconds");

	/// <summary>Reply when a countdown elapses and was meant to start the round.</summary>
	public static readonly Reply CountdownElapsedForRound = new("announce.countdownElapsedForRound",
		"Match cannot start because no beatmap has been selected.");

	/// <summary>Reply when a countdown elapses but wasn't meant to start the round.</summary>
	public static readonly Reply CountdownElapsed = new("announce.countdownElapsed", "Countdown finished");

	/// <summary>Reply when a countdown is cancelled.</summary>
	public static readonly Reply CountdownCancelled = new("announce.countdownCancelled", "Countdown aborted.");

	/// <summary>Reply when starting a round by hand cancels a countdown.</summary>
	public static readonly Reply CountdownCancelledByRoundStart = new("announce.countdownCancelledByRoundStart",
		"Countdown cancelled — the round was started.");

	/// <summary>Reply when aborting the round cancels a countdown.</summary>
	public static readonly Reply CountdownCancelledByRoundAbort = new("announce.countdownCancelledByRoundAbort",
		"Countdown cancelled — the round was aborted.");

	// ── Room round ─────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a round starts after a countdown.</summary>
	public static readonly Reply RoundStartedByCountdown =
		new("announce.roundStartedByCountdown", "Good luck, have fun!");

	/// <summary>Reply when a round cannot start because the room has no players.</summary>
	public static readonly Reply RoundStartedNoPlayers = new("announce.roundStartedNoPlayers",
		"Match cannot start because the room has no players.");

	/// <summary>Reply when a round is aborted.</summary>
	public static readonly Reply RoundAborted = new("announce.roundAborted", "Match aborted.");

	/// <summary>
	///     Reply for a player whose score did not come in before a round was decided; <c>{player}</c> and
	///     <c>{seconds}</c> are filled in.
	/// </summary>
	public static readonly Reply ScoreMissing = new("announce.scoreMissing",
		"{player} didn't submit a score (timeout: {seconds}s).", "player", "seconds");

	// ── Room settings ──────────────────────────────────────────────────────────────────────
	/// <summary>Reply when room settings change cancels a countdown that would start the round.</summary>
	public static readonly Reply SettingsChangedCountdownCancelled = new("announce.settingsChangedCountdownCancelled",
		"Match start cancelled — room settings changed.");

	// ── Room closing ───────────────────────────────────────────────────────────────────────
	/// <summary>Reply when an empty room announces it will close; <c>{minutes}</c> is minutes.</summary>
	public static readonly Reply RoomClosingAnnounced = new("announce.roomClosingAnnounced",
		"This room is empty and will close in {minutes} minutes unless a player joins.", "minutes");

	/// <summary>Reply when a player joins a room that was closing.</summary>
	public static readonly Reply RoomPlayerJoinedClosing = new("announce.roomPlayerJoinedClosing",
		"A player joined — the room will no longer close.");

	// ── Anticheat ──────────────────────────────────────────────────────────────────────────
	/// <summary>Reply in room channel when a player is flagged; <c>{player}</c> is player, <c>{reason}</c> is reason.</summary>
	public static readonly Reply AnticheatFlagRoom = new("announce.anticheatFlagRoom",
		"Anti-cheat flag for {player}: {reason}", "player", "reason");

	/// <summary>
	///     Reply in PM when a player is flagged in a match; <c>{room}</c> is room id, <c>{name}</c> is room name,
	///     <c>{player}</c> is player, <c>{reason}</c> is reason.
	/// </summary>
	public static readonly Reply AnticheatFlagPm = new("announce.anticheatFlagPm",
		"Anti-cheat flag in match #{room} {name}: {player} — {reason}", "room", "name", "player", "reason");
}