using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChannel : Channel
{
	/// <summary>The prefix every room channel name starts with.</summary>
	public const string NamePrefix = "#mp_";

	internal RoomChannel(Room room, ChannelContext context) : base(context)
	{
		Room = room;
	}

	/// <summary>Gets the room this channel belongs to.</summary>
	public Room Room { get; }

	/// <inheritdoc />
	public override string Name => NameFor(Room.Id);

	/// <inheritdoc />
	public override string Topic => Room.Match.Value.Name;

	/// <summary>Gets the channel name of a room.</summary>
	/// <param name="roomId">The room's id.</param>
	/// <returns>The channel name clients use for the room.</returns>
	public static string NameFor(int roomId)
	{
		return $"{NamePrefix}{roomId}";
	}

	/// <summary>Reads the room id out of a room channel name.</summary>
	/// <param name="name">The channel name.</param>
	/// <param name="roomId">The room's id, when the name is a room channel name.</param>
	/// <returns><see langword="true" /> when the name is a room channel name.</returns>
	public static bool TryParseName(string name, out int roomId)
	{
		roomId = 0;
		if (!name.StartsWith(NamePrefix, StringComparison.Ordinal)) return false;

		var digits = name.AsSpan(NamePrefix.Length);
		return !digits.IsEmpty && digits.IndexOfAnyExceptInRange('0', '9') < 0 && int.TryParse(digits, out roomId);
	}

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, DateTimeOffset now)
	{
		return Room.Involves(connection, now) ||
		       connection.User.EffectivePermissions(now).Allows(UserPermissions.RoomObserve);
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, DateTimeOffset now)
	{
		return Room.Involves(connection, now) ||
		       connection.User.EffectivePermissions(now).Allows(UserPermissions.ChatManagePostAnyRoom);
	}
}