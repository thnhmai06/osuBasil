using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public sealed class SpectatorChannel : Channel
{
	internal SpectatorChannel(StableSession host, ChannelContext context) : base(context)
	{
		Host = host;
	}

	public StableSession Host { get; }

	public override string Name => $"#spec_{Host.Record.Id}";

	public override string Topic => $"{Host.Record.Value.Name}'s spectator channel";

	internal override bool CanRead(Session connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection, Host) || Host.Spectators.Contains(connection);
	}

	internal override bool CanWrite(Session connection, UserPrivileges privileges)
	{
		return CanRead(connection, privileges);
	}
}