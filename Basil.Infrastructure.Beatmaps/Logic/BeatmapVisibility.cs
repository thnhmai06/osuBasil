using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal static class BeatmapVisibility
{
	public static bool MayViewHidden(IActor? actor, TimeProvider clock)
	{
		return actor is not null && BeatmapQuery.MayViewHidden(actor.EffectivePrivileges(clock.GetUtcNow()));
	}

	public static bool CanView(BeatmapsetRecord set, bool mayViewHidden)
	{
		return set.Visible || mayViewHidden;
	}

	public static bool CanView(BeatmapRecord map, bool mayViewHidden)
	{
		return map.IsVisible() || mayViewHidden;
	}
}