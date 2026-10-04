using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>The channel that receives a user's private messages.</summary>
public sealed class PmChannel : Channel
{
	/// <summary>Initializes the private-message channel of a user, named after the user.</summary>
	/// <param name="owner">The user who receives the messages.</param>
	[SetsRequiredMembers]
	public PmChannel(User owner)
	{
		Name = owner.Value.Name;
	}
}
