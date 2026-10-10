using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannelSession(UserSession owner) : ChannelSession
{
	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner => owner;

	public override PmChannel Channel { get; } = new(owner.User);
}