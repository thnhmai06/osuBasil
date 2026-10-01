using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>The chat channel shared by a player and the users spectating them.</summary>
public sealed class SpectatorChannel : Channel
{
	/// <summary>Initializes the spectator channel of a player.</summary>
	/// <param name="host">The player being spectated.</param>
	[SetsRequiredMembers]
	public SpectatorChannel(User host)
	{
		Name = $"spec_{host.Id}";
		Topic = $"{host.Value.Name}'s spectator channel";
	}
}
