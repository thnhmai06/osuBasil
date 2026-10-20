using Basil.Domain.Chat;

namespace Basil.Application.Objects.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannel : Channel
{
	internal GeneralChannel(GeneralChannelRecord record, ChannelContext context) : base(context)
	{
		Record = record;
	}

	/// <summary>Gets the stored identity and settings of this channel.</summary>
	public GeneralChannelRecord Record { get; }

	/// <inheritdoc />
	public override string Name => Record.Name;

	/// <inheritdoc />
	public override string Topic => Record.Value.Topic;
}