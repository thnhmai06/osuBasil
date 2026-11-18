using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Chat.Objects;

public sealed class PmChannel : Channel
{
	internal PmChannel(User owner, ChannelContext context) : base(context)
	{
		Owner = owner;
	}

	public User Owner { get; }

	public override string Name => Owner.Record.Value.Name;

	public override string Topic => string.Empty;

	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection.User, Owner) && connection.Type is not SessionType.StableTourney;
	}

	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return connection.IsOpen && Owner.IsOnline;
	}
}