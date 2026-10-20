using Basil.Application.Objects.Users;

namespace Basil.Application.Objects.Chat;

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
}