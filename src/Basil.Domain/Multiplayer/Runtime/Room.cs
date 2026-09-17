using System.Collections.Immutable;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Holds an osu! multiplayer match's room settings, live slot state and lifecycle flags — the
///     part of a live match that is business state rather than live-projection machinery.
/// </summary>
public sealed class Room
{
	/// <summary>Gets the players banned from this match.</summary>
	public ConcurrentSet<User> BannedUsers = [];

	/// <summary>Gets the players a referee has invited via <c>!mp invite</c>, see <see cref="IsPrivate" />.</summary>
	public ConcurrentSet<User> InvitedUsers = [];

	/// <summary>Gets the players granted referee authority for this match.</summary>
	public ConcurrentSet<User> Referees = [];

	/// <summary>Gets the players whose connections are tourney clients attached to this match.</summary>
	public ConcurrentSet<User> TourneyClients = [];

	/// <summary> The registry slot identifier assigned to a Room.</summary>
	public required int SlotId { get; init; }

	/// <summary>
	///     Gets or sets the persistent id for this Room, set once its database row is created.
	/// </summary>
	public required int Id { get; set; }

	/// <summary>Gets or sets the room's name.</summary>
	public required string Name { get; set; }

	/// <summary>Gets or sets the room's password.</summary>
	public required string Password { get; set; }

	/// <summary>Gets or sets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; set; }

	public MatchSettings Settings { get; init; } = new();

	/// <summary>
	///     Gets or sets a value that indicates whether the room is locked against new players
	///     entirely.
	/// </summary>
	public bool IsLocked { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the room is private.
	/// </summary>
	public bool IsPrivate { get; set; }

	/// <summary>
	///     Gets or sets the player who created this match, or <see langword="null" /> when the room was
	///     created via the HTTP API with no session behind it. Set once, right after the room is
	///     created.
	/// </summary>
	public User? Creator { get; set; }

	/// <summary>The current room host.</summary>
	public User? Host { get; set; }

	/// <summary>
	///     Gets or sets the Rounds.Id of the beatmap currently being played, or null when no round is
	///     in progress. Set at match start, when a new Round row is created per beatmap played and
	///     cleared at MatchComplete. Score submissions link to this, so score-to-round linking does
	///     not depend on any gather or wait step at MatchComplete.
	/// </summary>
	public int? CurrentRoundId { get; set; }

	/// <summary>Gets the room's invitation url.</summary>
	public string Url => $"osump://{SlotId}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string Embed => $"[{Url} {Name}]";

	/// <summary>Gets the match's 16 slots, in order.</summary>
	public ImmutableList<RoomSlot> Slots { get; } = [.. Enumerable.Range(0, 16).Select(_ => new RoomSlot())];

	/// <summary>Gets a value that indicates whether <paramref name="player" /> may issue <c>!mp</c> commands on this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player is a referee; otherwise, <see langword="false" />.</returns>
	public bool IsReferee(User player)
	{
		return Referees.Contains(player) || IsCreator(player);
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> created this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player created this match; otherwise, <see langword="false" />.</returns>
	public bool IsCreator(User player)
	{
		return Creator is not null && Creator.Equals(player);
	}
}