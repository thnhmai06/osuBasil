using Basil.Application.Rulesets.Ports;
using Basil.Application.Shared.Events;

namespace Basil.Application.Rulesets.Objects.Events;

public abstract record RulesetEvent : Event;

/// <summary>The difficulty of a beatmap was calculated; <paramref name="Analysis"/> is null when it could not be.</summary>
public sealed record BeatmapCalculated(int BeatmapId, BeatmapAnalysis? Analysis) : RulesetEvent;

/// <summary>The pp of a score was calculated; <paramref name="Pp"/> is null when it could not be.</summary>
public sealed record ScorePerformanceCalculated(Guid Key, double? Pp) : RulesetEvent;

/// <summary>The calculation stack was installed and is available without a restart.</summary>
public sealed record RulesetsUpdated(string Version) : RulesetEvent;