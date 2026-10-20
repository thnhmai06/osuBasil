using Basil.Application.Objects.Sessions;

namespace Basil.Application.Objects.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannel : Channel
{
	internal PmChannel(UserSession owner, ChannelContext context) : base(context)
	{
		Owner = owner;
	}

	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner { get; }

	/// <inheritdoc />
	public override string Name => Owner.User.Value.Name;

	/// <inheritdoc />
	public override string Topic => string.Empty;
}