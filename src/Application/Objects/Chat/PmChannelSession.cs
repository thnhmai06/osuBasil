using Basil.Application.Objects.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Objects.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannelSession : ChannelSession
{
	internal PmChannelSession(UserSession owner, ChannelContext context) : base(context)
	{
		Owner = owner;
		Channel = new PmChannel(owner.User);
	}

	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner { get; }

	/// <summary>Gets the private-message channel this session holds.</summary>
	public override PmChannel Channel { get; }
}
