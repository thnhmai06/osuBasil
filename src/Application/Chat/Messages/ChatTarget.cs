namespace Basil.Application.Chat.Messages;

public abstract record ChatTarget
{
	private ChatTarget()
	{
	}

	public sealed record General(string Name) : ChatTarget;

	public sealed record Room(int RoomId) : ChatTarget;

	public sealed record OwnRoom : ChatTarget;

	public sealed record OwnSpectators : ChatTarget;

	public sealed record Private(int RecipientId) : ChatTarget;
}