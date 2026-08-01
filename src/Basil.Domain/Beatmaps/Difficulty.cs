namespace Basil.Domain.Beatmaps;

/// <summary>
///     Gameplay-mechanical facts about a beatmap: ruleset, tempo, difficulty settings, and star
///     rating. <see cref="TotalLength" /> serializes as whole seconds on the wire — see
///     Basil.Application.Json.TimeSpanSecondsJsonConverter, registered globally rather than declared
///     here via attribute, since Basil.Domain has zero project references and can't see it.
/// </summary>
public sealed record Difficulty(
	GameMode Mode,
	double Bpm,
	TimeSpan TotalLength,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Sr);