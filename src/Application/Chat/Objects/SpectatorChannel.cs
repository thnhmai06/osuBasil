using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannel : Channel
{
	internal SpectatorChannel(StableConnection host, ChannelContext context) : base(context)
	{
		Host = host;
	}

	/// <summary>Gets the connection being spectated.</summary>
	public StableConnection Host { get; }

	/// <inheritdoc />
	public override string Name => $"#spec_{Host.Record.Id}";

	/// <inheritdoc />
	public override string Topic => $"{Host.Record.Value.Name}'s spectator channel";

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, UserPrivileges privileges)
	{
		return ReferenceEquals(connection, Host) || Host.Spectators.Contains(connection);
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, UserPrivileges privileges)
	{
		return CanRead(connection, privileges);
	}
}