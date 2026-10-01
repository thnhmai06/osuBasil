using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>The channel that receives a user's private messages.</summary>
/// <param name="owner">The user who receives the messages.</param>
public sealed class PmChannel(User owner) : Channel(owner.Value.Name)
{
	/// <inheritdoc />
	public override string Topic => string.Empty;
}