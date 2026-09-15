using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>
///     Validates and maps the wire-format match-create data the client sends into
///     <see cref="MatchCreationData" />, the business layer's own shape for it.
/// </summary>
public static class MatchCreationDataMapper
{
	/// <summary>Checks that a client's match-create data claims a plausible host and room name.</summary>
	/// <param name="data">The parsed wire data to validate.</param>
	/// <param name="expectedHostId">The host id the data must claim.</param>
	/// <returns>
	///     <see langword="true" /> when the host id matches and the name is short enough; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public static bool IsValid(MatchState data, int expectedHostId)
	{
		return data.HostId == expectedHostId && data.Name.Length <= MatchLifecycle.MaxMatchNameLength;
	}

	/// <summary>Maps a client's parsed match-create data onto its business shape.</summary>
	/// <param name="data">The wire data to map. Assumed already validated by <see cref="IsValid" />.</param>
	/// <returns>The equivalent <see cref="MatchCreationData" />.</returns>
	public static MatchCreationData ToCreationData(this MatchState data)
	{
		// data.MapId is the wire/protocol value: -1 is a real client's explicit "no beatmap chosen",
		// and 0 is what an HTTP creation request leaves as an unused placeholder (ids in this schema
		// auto-increment from 1, so 0 can never be a real beatmap either). Both mean "no map" at this
		// wire-to-domain boundary.
		var mapId = data.MapId <= 0 ? null : (int?)data.MapId;

		return new MatchCreationData(
			data.Name, data.Password, data.MapName, mapId, data.MapMd5, data.HostId,
			(GameMode)data.Mode, (Mods)data.Mods, (MatchWinCondition)data.WinCondition,
			(MatchTeamType)data.TeamType, data.FreeMods, data.Seed);
	}
}
