using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public sealed class SpectatorChannel : Channel
{
	internal SpectatorChannel(StableConnection host, ChannelContext context) : base(context)
	{
		Host = host;
	}

	public StableConnection Host { get; }

	public override string Name => $"#spec_{Host.Record.Id}";

	public override string Topic => $"{Host.Record.Value.Name}'s spectator channel";

	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection, Host) || Host.Spectators.Contains(connection);
	}

	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return CanRead(connection, privileges);
	}
}