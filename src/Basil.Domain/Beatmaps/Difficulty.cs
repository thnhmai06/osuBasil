using System.Text.Json.Serialization;
using Basil.Domain.Json;

namespace Basil.Domain.Beatmaps;

/// <summary>Gameplay-mechanical facts about a beatmap: ruleset, tempo, difficulty settings, and star rating.</summary>
public sealed record Difficulty(
	GameMode Mode,
	double Bpm,
	[property: JsonConverter(typeof(TimeSpanSecondsJsonConverter))]
	TimeSpan TotalLength,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Sr);