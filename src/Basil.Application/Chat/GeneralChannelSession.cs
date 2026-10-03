using Basil.Domain.Chat;

namespace Basil.Application.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannelSession(GeneralChannel channel) : ChannelSession(channel)
{
	/// <summary>Gets the configured channel this session runs.</summary>
	public new GeneralChannel Channel => channel;
}