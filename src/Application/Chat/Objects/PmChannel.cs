using Basil.Domain.Authentication;
using Basil.Domain.Game.Users;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using User = Basil.Application.Users.Objects.User;

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

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection.User, Owner) && connection.Type is not SessionType.StableTourney;
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return connection.IsOpen && Owner.IsOnline;
	}
}