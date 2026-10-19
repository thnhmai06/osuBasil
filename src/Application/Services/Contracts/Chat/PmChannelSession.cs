using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChannelSession(UserSession owner) : ChannelSession
{
	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner => owner;

	/// <summary>Gets the private-message channel this session holds.</summary>
	public override PmChannel Channel { get; } = new(owner.User);
}
