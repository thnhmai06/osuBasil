using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>The chat channel shared by a player and the users spectating them.</summary>
/// <param name="host">The player being spectated.</param>
public sealed class SpectatorChatChannel(User host) : ChatChannel($"spec_{host.Id}")
{
	/// <inheritdoc />
	public override string Topic => $"{host.Value.Name}'s spectator channel";
}