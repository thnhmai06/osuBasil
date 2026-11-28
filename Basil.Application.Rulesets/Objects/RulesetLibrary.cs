using Basil.Application.Rulesets.Capabilities;
using Basil.Application.Rulesets.Objects.Events;
using Basil.Application.Rulesets.Ports;
using Basil.Application.Rulesets.Services;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Events;

namespace Basil.Application.Rulesets.Objects;

/// <summary>The osu! rulesets the server calculates with: their versions, their updates and the results of what they calculate.</summary>
public sealed class RulesetLibrary
{
	internal RulesetLibrary(TimeProvider clock, IRulesetUpdater updater, IEventPublisher<RulesetEvent> events)
	{
		Clock = clock;
		Updater = updater;
		Events = events;
	}

	internal TimeProvider Clock { get; }

	internal IRulesetUpdater Updater { get; }

	internal IEventPublisher<RulesetEvent> Events { get; }

	public ScopedRulesets ScopeTo(IActor actor)
	{
		return new ScopedRulesets(this, actor);
	}

	internal void Updated(string version)
	{
		Events.Publish(new RulesetsUpdated(version));
	}

	internal void BeatmapCalculated(int beatmapId, BeatmapAnalysis? analysis)
	{
		Events.Publish(new BeatmapCalculated(beatmapId, analysis));
	}

	internal void ScorePerformanceCalculated(Guid key, double? pp)
	{
		Events.Publish(new ScorePerformanceCalculated(key, pp));
	}
}