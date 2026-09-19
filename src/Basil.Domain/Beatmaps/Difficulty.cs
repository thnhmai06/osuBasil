using Basil.Domain.Mechanics;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Describes the gameplay-mechanical facts about a beatmap.
/// </summary>
/// <param name="Mode">The game mode the beatmap is played in.</param>
/// <param name="Bpm">The beats per minute of the beatmap.</param>
/// <param name="Length">The total length of the beatmap.</param>
/// <param name="Cs">The circle size setting.</param>
/// <param name="Ar">The approach rate setting.</param>
/// <param name="Od">The overall difficulty setting.</param>
/// <param name="Hp">The health drain rate setting.</param>
/// <param name="Star">The star rating of the beatmap.</param>
/// <remarks>
///     <see cref="Length" /> serializes as a whole number of seconds on the wire.
/// </remarks>
public readonly record struct Difficulty(
	GameMode Mode,
	double Bpm,
	TimeSpan Length,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Star);