using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;

namespace Basil.Application.Beatmaps.Objects;

/// <summary>The osu! rulesets that compute star ratings, and the recomputation of stored beatmaps with them.</summary>
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

	/// <summary>Scopes the rulesets to an actor and its authority.</summary>
	/// <param name="actor">The actor acting.</param>
	/// <returns>A short-lived scope for the actor.</returns>
	public ScopedRulesets ScopeTo(IActor actor)
	{
		return new ScopedRulesets(this, actor);
	}
}
