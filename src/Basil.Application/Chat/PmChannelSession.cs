using Basil.Application.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannelSession(UserSession owner) : ChannelSession(new PmChannel(owner.User))
{
	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner => owner;
}