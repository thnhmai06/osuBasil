namespace Basil.Domain.Beatmaps;

/// <summary>
///     Describes the gameplay-mechanical facts about a beatmap.
/// </summary>
/// <param name="Mode">The game mode the beatmap is played in.</param>
/// <param name="Bpm">The beats per minute of the beatmap.</param>
/// <param name="TotalLength">The total length of the beatmap.</param>
/// <param name="Cs">The circle size setting.</param>
/// <param name="Ar">The approach rate setting.</param>
/// <param name="Od">The overall difficulty setting.</param>
/// <param name="Hp">The health drain rate setting.</param>
/// <param name="Sr">The star rating of the beatmap.</param>
/// <remarks>
///     <see cref="TotalLength" /> serializes as whole seconds on the wire, via
///     Basil.Application.Json.TimeSpanSecondsJsonConverter. The converter is registered globally
///     rather than declared here with an attribute because Basil.Domain has no project references
///     and cannot see it.
/// </remarks>
public sealed record Difficulty(
	GameMode Mode,
	double Bpm,
	TimeSpan TotalLength,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Sr);
