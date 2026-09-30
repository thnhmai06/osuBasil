using Basil.Domain.Chat;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>A user's private-message channel: every message posted here is delivered to that user.</summary>
public sealed class PmChatChannelSession(UserSession owner) : ChatChannelSession(new PmChatChannel(owner.User))
{
	/// <summary>Gets the online session of the user who receives the messages.</summary>
	public UserSession Owner => owner;

	/// <inheritdoc />
	/// <remarks>Only the owner's own connections read the channel, and osu!tourney clients do not receive private messages.</remarks>
	public override bool CanRead(Connection connection) =>
		ReferenceEquals(connection.Session, owner) && connection.Type is not ConnectionType.Tourney;

	/// <inheritdoc />
	/// <remarks>Any open connection may send the owner a private message.</remarks>
	public override bool CanWrite(Connection connection) => connection.IsOpen;

	/// <inheritdoc />
	protected override bool PostRequiresMembership => false;

	/// <inheritdoc />
	protected override bool AcceptsMessages => !(owner.User.Value.SilenceEndsAt > DateTimeOffset.UtcNow);

	/// <summary>Replies with the owner's away message, if any, in the sender's own private-message channel.</summary>
	private protected override void OnPosted(Connection by, DateTimeOffset now)
	{
		if (owner.AwayMessage is { } away && !ReferenceEquals(by.Session, owner))
			by.Session.PmChannel.Emit(new MessagePosted(by.Session.PmChannel, new ChatMessage(owner.User, away, now),
				false));
	}
}