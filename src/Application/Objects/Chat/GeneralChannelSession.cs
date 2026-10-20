using Basil.Domain.Chat;

namespace Basil.Application.Objects.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannelSession : ChannelSession
{
	internal GeneralChannelSession(GeneralChannel channel, ChannelContext context) : base(context)
	{
		Channel = channel;
	}

	/// <summary>Gets the configured channel this session runs.</summary>
	public override GeneralChannel Channel { get; }
}
