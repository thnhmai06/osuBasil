using Basil.Server.Shared.Eventing;

namespace Basil.Server.Features.Multiplayer;

/// <summary>The <see cref="ILiveEventHub" /> stream identifying each of a match's live channels.</summary>
internal static class MatchStreams
{
	/// <summary>The <see cref="StreamKey.Category" /> shared by every match stream, also used with <see cref="ILiveEventHub.Forget" /> at teardown.</summary>
	public const string Category = "match";

	public static StreamKey Main(int matchDbId) => new(Category, matchDbId, "main");
	public static StreamKey Settings(int matchDbId) => new(Category, matchDbId, "settings");
	public static StreamKey Host(int matchDbId) => new(Category, matchDbId, "host");
	public static StreamKey Refs(int matchDbId) => new(Category, matchDbId, "refs");
	public static StreamKey Bans(int matchDbId) => new(Category, matchDbId, "bans");
	public static StreamKey Timer(int matchDbId) => new(Category, matchDbId, "timer");
	public static StreamKey Slots(int matchDbId) => new(Category, matchDbId, "slots");
	public static StreamKey Chat(int matchDbId) => new(Category, matchDbId, "chat");

	/// <summary>The per-slot "slot" sub-event channel, indexed 0-based like <see cref="MatchSession.Slots" />.</summary>
	public static StreamKey Slot(int matchDbId, int slotIndex) => new(Category, matchDbId, $"slot:{slotIndex}");

	/// <summary>
	///     The per-slot live-score channel: keyed by slot rather than by occupant, since occupancy can
	///     change mid-match and a fixed slot index needs no dynamic re-subscription.
	/// </summary>
	public static StreamKey Score(int matchDbId, int slotIndex) => new(Category, matchDbId, $"score:{slotIndex}");
}
