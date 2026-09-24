using System.Collections.Immutable;
using Basil.Domain.Chat;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Holds an osu! multiplayer match's room settings, live slot state and lifecycle flags — the
///     part of a live match that is business state rather than live-projection machinery.
/// </summary>
public sealed class Room
{
	/// <summary>Gets the registry slot identifier assigned to this room.</summary>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Room Id must be positive.");
	}

	public Room()
	{
		Channel = new RoomChannel(this);
	}

	#region Settings

	/// <summary>The match this room refers to.</summary>
	public required Match Match { get; init; }

	/// <summary>
	///     Sets the room's password, which a client must supply to join.
	/// </summary>
	/// <remarks>
	///     The getter is private; consumers write the password, and <see cref="Url" /> incorporates
	///     it into the join URL.
	/// </remarks>
	public string Password { private get; set; } = string.Empty;

	/// <summary>Gets a value indicating whether the room is protected by a non-empty, non-whitespace password.</summary>
	public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

	/// <summary>Gets or sets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; set; } = false;

	/// <summary>Gets the room's settings, including the selected beatmap, mode, and mods.</summary>
	public RoomSettings Settings { get; init; } = new();

	public readonly RoomChannel Channel;

	/// <summary>
	///     Gets or sets a value that indicates whether the room is locked against new players
	///     entirely.
	/// </summary>
	public bool IsLocked { get; set; }

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
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

	/// <summary>The players banned from this match.</summary>
	public readonly ConcurrentSet<User> BannedUsers = [];

	/// <summary>The players a referee has invited via <c>!mp invite</c>.</summary>
	public readonly ConcurrentSet<User> InvitedUsers = [];

	/// <summary>The players whose connections are tourney clients attached to this match.</summary>
	public readonly ConcurrentSet<User> TourneyUsers = [];

	/// <summary>The players granted referee authority for this match.</summary>
	public readonly ConcurrentSet<User> Referees = [];

	/// <summary>The match's 16 slots, in order.</summary>
	public readonly ImmutableList<RoomSlot> Slots = [.. Enumerable.Range(0, 16).Select(_ => new RoomSlot())];

	/// <summary>Gets a value indicating whether every slot is occupied.</summary>
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
	/// <returns>
	///     <see langword="true" /> if the player is a referee or the creator of this match;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool HasRefereePermission(User player)
	{
		return Referees.Contains(player) || IsCreator(player);
	}

	/// <summary>
	///     Gets a value indicating whether <paramref name="player" /> may join this room.
	/// </summary>
	/// <remarks>
	///     Banned players can never join. A referee or the creator can always join. Any other player
	///     may join only when the room is unlocked or they have been invited.
	/// </remarks>
	/// <param name="player">The player to check.</param>
	/// <returns>
	///     <see langword="true" /> if the player may join; otherwise, <see langword="false" />.
	/// </returns>
	public bool HasJoinPermission(User player)
	{
		if (BannedUsers.Contains(player)) return false;
		return HasRefereePermission(player) || !IsLocked || InvitedUsers.Contains(player);
	}

	#endregion
}