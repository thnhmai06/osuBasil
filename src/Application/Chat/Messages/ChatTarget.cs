using Basil.Domain.Users;

namespace Basil.Application.Chat.Messages;

/// <summary>Where a chat message goes, addressed by identity and not by the spelling of one transport.</summary>
public abstract record ChatTarget
{
	private ChatTarget()
	{
	}

	/// <summary>A general channel, identified by its name.</summary>
	/// <param name="Name">The channel name, with its leading <c>#</c>.</param>
	public sealed record General(string Name) : ChatTarget;

	/// <summary>The channel of an open multiplayer room.</summary>
	/// <param name="RoomId">The room's id.</param>
	public sealed record Room(int RoomId) : ChatTarget;

	/// <summary>The channel of the room the sender is in.</summary>
	public sealed record OwnRoom : ChatTarget;

	/// <summary>The spectator channel of the stream the sender is part of.</summary>
	public sealed record OwnSpectators : ChatTarget;

	/// <summary>A private message to a user.</summary>
	/// <param name="Recipient">The user who receives the message.</param>
	public sealed record Private(UserRecord Recipient) : ChatTarget;
}
