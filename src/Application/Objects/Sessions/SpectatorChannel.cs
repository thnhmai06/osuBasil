using Basil.Application.Objects.Chat;

namespace Basil.Application.Objects.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannel : Channel
{
	internal SpectatorChannel(BanchoConnection host, ChannelContext context) : base(context)
	{
		Host = host;
	}

	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host { get; }

	/// <inheritdoc />
	public override string Name => $"#spec_{Host.Record.Id}";

	/// <inheritdoc />
	public override string Topic => $"{Host.Record.Value.Name}'s spectator channel";
}