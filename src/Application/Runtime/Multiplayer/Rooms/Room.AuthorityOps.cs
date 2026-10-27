using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Authority transitions: referees and host.</content>
public sealed partial class Room
{
	/// <summary>Makes a user a referee.</summary>
	/// <returns>Ok, IsCreator, AlreadyReferee, TooManyReferees or RoomClosed.</returns>
	internal Result<RoomOutcome> AddReferee(UserRecord by, UserRecord user)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.Creator.Equals(user)) return RoomOutcome.IsCreator;
			if (Authority.Referees.Contains(user)) return RoomOutcome.AlreadyReferee;
			if (Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomOutcome.TooManyReferees;

			Authority.MutableReferees.Add(user);
			Emit(new RoomRefereeAdded(this, by, user));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Removes a user from the referees.</summary>
	/// <returns>Ok, NotReferee or RoomClosed.</returns>
	internal Result<RoomOutcome> RemoveReferee(UserRecord by, UserRecord user)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Authority.MutableReferees.Remove(user)) return RoomOutcome.NotReferee;

			Emit(new RoomRefereeRemoved(this, by, user));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <returns>Ok, NotInRoom or RoomClosed; giving host to the current host does nothing.</returns>
	internal Result<RoomOutcome> SetHost(UserRecord by, BanchoConnection? host)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (host is not null && Slots.Find(host) is null) return RoomOutcome.NotInRoom;
			if (ReferenceEquals(Authority.Host, host)) return RoomOutcome.Ok;

			Authority.Host = host;
			Emit(new RoomHostChanged(this, by, host));
			return RoomOutcome.Ok;
		}
	}
}