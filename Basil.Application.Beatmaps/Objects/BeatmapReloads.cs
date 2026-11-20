using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Shared.Actors;

namespace Basil.Application.Beatmaps.Objects;

public sealed class BeatmapReloads
{
	internal BeatmapReloads(TimeProvider clock, IBeatmapReloader reloader)
	{
		Clock = clock;
		Reloader = reloader;
	}

	internal TimeProvider Clock { get; }

	internal IBeatmapReloader Reloader { get; }

	public ScopedBeatmapReloads ScopeTo(IActor actor)
	{
		return new ScopedBeatmapReloads(this, actor);
	}

	internal RulesetUpdateResult Reload()
	{
		return Reloader.TryStart(false) ? RulesetUpdateResult.Ok : RulesetUpdateResult.Busy;
	}
}