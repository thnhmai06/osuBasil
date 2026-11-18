using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Chat.Objects;

public sealed class RoomChannel : Channel
{
	public const string NamePrefix = "#mp_";

	internal RoomChannel(Room room, ChannelContext context) : base(context)
	{
		Room = room;
	}

	public Room Room { get; }

	public override string Name => NameFor(Room.Id);

	public override string Topic => Room.Match.Value.Name;

	public static string NameFor(int roomId)
	{
		return $"{NamePrefix}{roomId}";
	}

	public static bool TryParseName(string name, out int roomId)
	{
		roomId = 0;
		if (!name.StartsWith(NamePrefix, StringComparison.Ordinal)) return false;

		var digits = name.AsSpan(NamePrefix.Length);
		return !digits.IsEmpty && digits.IndexOfAnyExceptInRange('0', '9') < 0 && int.TryParse(digits, out roomId);
	}

	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges) || privileges.Allows(UserPrivileges.Tournament);
	}

	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return Room.Involves(connection, privileges) || privileges.Allows(UserPrivileges.Tournament);
	}
}