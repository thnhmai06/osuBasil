using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Users;

namespace Basil.Application.Runtime.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannel : Channel
{
	internal PmChannel(User owner, ChannelContext context) : base(context)
	{
		Owner = owner;
	}

	/// <summary>Gets the user who receives the messages.</summary>
	public User Owner { get; }

	/// <inheritdoc />
	public override string Name => Owner.Record.Value.Name;

	/// <inheritdoc />
	public override string Topic => string.Empty;

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, DateTimeOffset now)
	{
		return ReferenceEquals(connection.User, Owner) && connection.Type is not ConnectionType.Tourney;
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, DateTimeOffset now)
	{
		return connection.IsOpen && Owner.IsOnline;
	}
}