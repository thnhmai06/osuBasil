using Basil.Domain.Multiplayer.Matches;

namespace Basil.Application.Storage.Contracts.Multiplayer.Rooms;

/// <summary>The settings a room plays with: those stored on its match and those that live only while it is open.</summary>
public sealed class RoomSettings
{
	private readonly Match _match;

	/// <summary>Creates the settings of a room.</summary>
	/// <param name="match">The match the room plays.</param>
	/// <param name="matchSettings">The room's initial stored settings.</param>
	internal RoomSettings(Match match, MatchSettings matchSettings)
	{
		_match = match;
		MatchSettings = matchSettings;
	}

	/// <summary>Gets the stored settings of the room's match.</summary>
	public MatchSettings MatchSettings { get; }

	/// <summary>Gets the name broadcast to clients.</summary>
	public string Name => _match.Value.Name;

	/// <summary>Gets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate => _match.Value.IsPrivate;

	/// <summary>Gets the room's password, or an empty string for none.</summary>
	public string Password { get; internal set; } = string.Empty;

	/// <summary>Gets the currently selected beatmap.</summary>
	public BeatmapReference? Beatmap { get; internal set; }
}