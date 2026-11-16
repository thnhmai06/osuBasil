using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Paging;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Registry;

/// <summary>Which open rooms a room listing includes.</summary>
/// <param name="Text">Text contained in the room's name (ignoring case), or <see langword="null" /> for any.</param>
/// <param name="Host">The name or id of the room's host, or <see langword="null" /> for any.</param>
/// <param name="Creator">The name or id of the room's creator, or <see langword="null" /> for any.</param>
/// <param name="Player">The name or id of a player seated in the room, or <see langword="null" /> for any.</param>
/// <param name="Status">What the room is doing, or <see langword="null" /> for any.</param>
/// <param name="Mode">The game mode of the room, or <see langword="null" /> for any.</param>
/// <param name="Password">
///     Whether only rooms with a password (<see langword="true" />) or without one (<see langword="false" />) are
///     included, or <see langword="null" /> for both.
/// </param>
public sealed record RoomQuery(
	string? Text = null,
	string? Host = null,
	string? Creator = null,
	string? Player = null,
	RoomStatus? Status = null,
	GameMode? Mode = null,
	bool? Password = null) : IParsable<RoomQuery>
{
	/// <summary>Reads a query written in the room search syntax.</summary>
	/// <param name="s">
	///     The search text, for example <c>host:bob status:open mode:0 password:no cup</c>; the free text matches the
	///     room's name, <c>host:</c>, <c>creator:</c> and <c>player:</c> take a name or an id, <c>status:</c> takes
	///     <c>open</c>, <c>locked</c> or <c>playing</c>, <c>mode:</c> takes <c>0</c> to <c>3</c> and <c>password:</c> takes
	///     <c>yes</c> or <c>no</c>.
	/// </param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <returns>The query; a filter with an unknown key or a value that does not parse stays in <see cref="Text" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
	public static RoomQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		string? host = null, creator = null, player = null;
		RoomStatus? status = null;
		GameMode? mode = null;
		bool? password = null;

		var text = SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			if (op is not (":" or "=")) return false;
			switch (key)
			{
				case "host":
					host = rawValue;
					return true;
				case "creator":
					creator = rawValue;
					return true;
				case "player":
					player = rawValue;
					return true;
				case "status" when Enum.TryParse<RoomStatus>(rawValue, true, out var parsed) &&
				                   Enum.IsDefined(parsed):
					status = parsed;
					return true;
				case "mode" when byte.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture,
					                 out var number) && Enum.IsDefined((GameMode)number):
					mode = (GameMode)number;
					return true;
				case "password" when rawValue.ToLowerInvariant() is "yes" or "no":
					password = rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
					return true;
				default:
					return false;
			}
		});

		return new RoomQuery(text, host, creator, player, status, mode, password);
	}

	/// <summary>Reads a query written in the room search syntax.</summary>
	/// <param name="s">The search text.</param>
	/// <param name="provider">Not used; the syntax does not depend on culture.</param>
	/// <param name="result">The query, or <see langword="null" /> when <paramref name="s" /> is <see langword="null" />.</param>
	/// <returns><see langword="true" /> unless <paramref name="s" /> is <see langword="null" />; any text reads as a query.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out RoomQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	/// <summary>Tells whether an open room is one the query includes.</summary>
	/// <param name="room">The room.</param>
	/// <returns><see langword="true" /> when the room satisfies every filter of the query.</returns>
	public bool Includes(Room room)
	{
		ArgumentNullException.ThrowIfNull(room);

		var status = room.Rounds.InProgress ? RoomStatus.Playing
			: room.Slots.Locked ? RoomStatus.Locked
			: RoomStatus.Open;
		return (Text is null || room.Settings.Name.Contains(Text, StringComparison.OrdinalIgnoreCase))
		       && (Host is null || room.Authority.Host is { } host && Names(host.Record, Host))
		       && (Creator is null || Names(room.Authority.Creator, Creator))
		       && (Player is null || room.Slots.Any(slot => slot.Player is { } seated && Names(seated.Record, Player)))
		       && (Status is null || Status == status)
		       && (Mode is null || Mode == room.Settings.MatchSettings.Mode)
		       && (Password is null || Password == room.Settings.Password.Length > 0);
	}

	private static bool Names(UserRecord user, string reference)
	{
		return (int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && user.Id == id)
		       || user.Value.Name.Equals(reference, StringComparison.OrdinalIgnoreCase);
	}
}
