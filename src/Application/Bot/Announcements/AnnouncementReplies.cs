using Basil.Application.Bot.Replies;

namespace Basil.Application.Bot.Announcements;

internal static class AnnouncementReplies
{
	// ── Room countdown ─────────────────────────────────────────────────────────────────────
	public static readonly Reply CountdownStartedForRound = new("announce.countdownStartedForRound",
		"Queued the match to start in {seconds} seconds", "seconds");

	public static readonly Reply CountdownStarted = new("announce.countdownStarted",
		"Started a {seconds}-second countdown.", "seconds");

	public static readonly Reply CountdownTickedForRound = new("announce.countdownTickedForRound",
		"Match starts in {seconds} seconds", "seconds");

	public static readonly Reply CountdownTicked =
		new("announce.countdownTicked", "{seconds} seconds remaining", "seconds");

	public static readonly Reply CountdownElapsedForRound = new("announce.countdownElapsedForRound",
		"Match cannot start because no beatmap has been selected.");

	public static readonly Reply CountdownElapsed = new("announce.countdownElapsed", "Countdown finished");

	public static readonly Reply CountdownCancelled = new("announce.countdownCancelled", "Countdown aborted.");

	public static readonly Reply CountdownReplaced = new("announce.countdownReplaced",
		"Countdown cancelled with {seconds} seconds remaining — a new countdown is starting.", "seconds");

	public static readonly Reply CountdownCancelledByRoundStart = new("announce.countdownCancelledByRoundStart",
		"Countdown cancelled — the round was started.");

	public static readonly Reply CountdownCancelledByRoundAbort = new("announce.countdownCancelledByRoundAbort",
		"Countdown cancelled — the round was aborted.");

	// ── Room round ─────────────────────────────────────────────────────────────────────────
	public static readonly Reply RoundStartedByCountdown =
		new("announce.roundStartedByCountdown", "Good luck, have fun!");

	public static readonly Reply RoundStartedNoPlayers = new("announce.roundStartedNoPlayers",
		"Match cannot start because the room has no players.");

	public static readonly Reply RoundAborted = new("announce.roundAborted", "Match aborted.");

	public static readonly Reply RoundEnded = new("announce.roundEnded",
		"Round #{id} has ended. Please wait {seconds} seconds while the final results arrive.", "id", "seconds");

	public static readonly Reply ScoresReceived = new("announce.scoresReceived", "All scores have been received.");

	public static readonly Reply ScoresReceivedExcept = new("announce.scoresReceivedExcept",
		"All scores have been received except from:");

	public static readonly Reply ScoreMissing = new("announce.scoreMissing", "{player}", "player");

	// ── Room settings ──────────────────────────────────────────────────────────────────────
	public static readonly Reply SettingsChangedCountdownCancelled = new("announce.settingsChangedCountdownCancelled",
		"Match start cancelled — room settings changed.");

	// ── Room closing ───────────────────────────────────────────────────────────────────────
	public static readonly Reply RoomClosingAnnounced = new("announce.roomClosingAnnounced",
		"This room is empty and will close in {minutes} minutes unless a player joins.", "minutes");

	public static readonly Reply RoomPlayerJoinedClosing = new("announce.roomPlayerJoinedClosing",
		"A player joined — the room will no longer close.");

	// ── Anticheat ──────────────────────────────────────────────────────────────────────────
	public static readonly Reply AnticheatFlagRoom = new("announce.anticheatFlagRoom",
		"Anti-cheat flag for {player}: {reason}", "player", "reason");

	public static readonly Reply AnticheatFlagPm = new("announce.anticheatFlagPm",
		"Anti-cheat flag in match #{room} {name}: {player} — {reason}", "room", "name", "player", "reason");
}