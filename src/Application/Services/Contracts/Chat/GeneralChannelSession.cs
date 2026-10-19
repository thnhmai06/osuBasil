using Basil.Domain.Chat;

namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannelSession(GeneralChannel channel) : ChannelSession
{
	/// <summary>Gets the configured channel this session runs.</summary>
	public override GeneralChannel Channel => channel;
}