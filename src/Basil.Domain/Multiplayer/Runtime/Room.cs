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
	/// <summary> The registry slot identifier assigned to a Room.</summary>
	public required int Id { get; init; }

	#region Settings

	/// <summary>The match which this room refer to.</summary>
	public required Match Match { get; init; }

	/// <summary>Gets or sets the room's password.</summary>
	public required string Password { private get; set; }

	public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

	/// <summary>Gets or sets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; set; }

	public MatchSettings Settings { get; init; } = new();

	/// <summary>
	///     Gets or sets a value that indicates whether the room is locked against new players
	///     entirely.
	/// </summary>
	public bool IsLocked { get; set; }

	/// <summary>Gets the room's join url.</summary>
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string Embed => $"({Match.Name})[{Url}]";

	#endregion

	#region Users

	/// <summary>
	///     Gets or sets the player who created this match, or <see langword="null" /> when the room was
	///     created via the HTTP API with no session behind it. Set once, right after the room is
	///     created.
	/// </summary>
	public required User? Creator { get; init; }

	/// <summary>The current room host.</summary>
	public User? Host { get; set; }

	/// <summary>Gets the players banned from this match.</summary>
	public readonly ConcurrentSet<User> BannedUsers = [];

	/// <summary>Gets the players a referee has invited via <c>!mp invite</c>, see <see cref="Match.IsVisible" />.</summary>
	public readonly ConcurrentSet<User> InvitedUsers = [];

	/// <summary>Gets the players whose connections are tourney clients attached to this match.</summary>
	public readonly ConcurrentSet<User> TourneyUsers = [];

	/// <summary>Gets the players granted referee authority for this match.</summary>
	public readonly ConcurrentSet<User> Referees = [];

	/// <summary>Gets the match's 16 slots, in order.</summary>
	public readonly ImmutableList<RoomSlot> Slots = [.. Enumerable.Range(0, 16).Select(_ => new RoomSlot())];

	public bool IsFullSlots => !Slots.Any(s => s.IsEmpty);

	/// <summary>Gets a value that indicates whether <paramref name="player" /> created this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player created this match; otherwise, <see langword="false" />.</returns>
	public bool IsCreator(User player)
	{
		return Creator is not null && Creator.Equals(player);
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> may issue <c>!mp</c> commands on this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player is a referee; otherwise, <see langword="false" />.</returns>
	public bool HasRefereePermission(User player)
	{
		return Referees.Contains(player) || IsCreator(player);
	}

	public bool HasJoinPermission(User player)
	{
		if (BannedUsers.Contains(player)) return false;
		return HasRefereePermission(player) || !IsLocked || InvitedUsers.Contains(player);
	}

	#endregion
}