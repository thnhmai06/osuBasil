using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Shared.Actors;

namespace Basil.Application.Beatmaps.Objects;

public sealed class Rulesets
{
	internal Rulesets(TimeProvider clock, IRulesetUpdater updater, IBeatmapReloader reloader)
	{
		Clock = clock;
		Updater = updater;
		Reloader = reloader;
	}

	internal TimeProvider Clock { get; }

	internal IRulesetUpdater Updater { get; }

	internal IBeatmapReloader Reloader { get; }

	public ScopedRulesets ScopeTo(IActor actor)
	{
		return new ScopedRulesets(this, actor);
	}

	internal RulesetUpdateResult Reload()
	{
		return Reloader.TryStart() ? RulesetUpdateResult.Ok : RulesetUpdateResult.Busy;
	}
}