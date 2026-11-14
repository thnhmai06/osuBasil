using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

/// <summary>Decides which beatmaps and beatmapsets a caller may see.</summary>
internal static class BeatmapVisibility
{
	/// <summary>Tells whether the caller may see hidden beatmaps.</summary>
	/// <param name="actor">The caller, or <see langword="null" /> for a caller without a session.</param>
	/// <param name="clock">The clock the caller's privileges are evaluated at.</param>
	public static bool MayViewHidden(IActor? actor, TimeProvider clock)
	{
		return actor is not null && BeatmapQuery.MayViewHidden(actor.EffectivePrivileges(clock.GetUtcNow()));
	}

	/// <summary>Tells whether a caller may see a beatmapset.</summary>
	public static bool CanView(BeatmapsetRecord set, bool mayViewHidden)
	{
		return set.Visible || mayViewHidden;
	}

	/// <summary>Tells whether a caller may see a beatmap, which is hidden when it or its beatmapset is.</summary>
	public static bool CanView(BeatmapRecord map, bool mayViewHidden)
	{
		return map.IsVisible() || mayViewHidden;
	}
}