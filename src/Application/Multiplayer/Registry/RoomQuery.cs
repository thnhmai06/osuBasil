using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Paging;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Registry;

public sealed record RoomQuery(
	string? Text = null,
	string? Host = null,
	string? Creator = null,
	string? Player = null,
	RoomStatus? Status = null,
	GameMode? Mode = null,
	bool? Password = null) : IParsable<RoomQuery>
{
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

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out RoomQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	public bool Includes(Room room)
	{
		ArgumentNullException.ThrowIfNull(room);

		var status = room.Rounds.InProgress ? RoomStatus.Playing
			: room.Slots.Locked ? RoomStatus.Locked
			: RoomStatus.Open;
		return (Text is null || room.Settings.Name.Contains(Text, StringComparison.OrdinalIgnoreCase))
		       && (Host is null || (room.Authority.Host is { } host && Names(host, Host)))
		       && (Creator is null || Names(room.Authority.Creator, Creator))
		       && (Player is null || room.Slots.Any(slot => slot.Player is { } seated && Names(seated, Player)))
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