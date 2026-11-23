using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Session = Basil.Application.Sessions.Objects.Session;
using UserSessions = Basil.Application.Sessions.Objects.UserSessions;

namespace Basil.Application.Chat.Objects;

public sealed class PmChannel : Channel
{
	internal PmChannel(UserSessions owner, ChannelContext context) : base(context)
	{
		Owner = owner;
	}

	public UserSessions Owner { get; }

	public override string Name => Owner.Record.Value.Name;

	public override string Topic => string.Empty;

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection.Siblings, Owner) && connection.Type is not SessionType.StableTourney;
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return connection.IsOpen && Owner.IsOnline;
	}
}