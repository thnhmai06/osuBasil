using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Multiplayer;

namespace Basil.Domain.Chat;

/// <summary>The chat channel of a multiplayer room.</summary>
public sealed class RoomChannel : Channel
{
	private readonly Match _match;

	/// <summary>Initializes the channel of a room, named after the room's id.</summary>
	/// <param name="roomId">The id of the room.</param>
	/// <param name="match">The match the room plays; its name is the channel's topic.</param>
	[SetsRequiredMembers]
	public RoomChannel(int roomId, Match match)
	{
		_match = match;
		Name = $"#mp_{roomId}";
	}

	/// <summary>The topic of a room's channel is its match's name.</summary>
	public override string Topic
	{
		get => _match.Value.Name;
		set => _match.Value.Name = value;
	}
}