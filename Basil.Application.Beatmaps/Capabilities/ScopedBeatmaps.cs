using Basil.Application.Shared.Actors;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>The beatmap catalog as one caller may read it; a caller without an actor is an anonymous reader.</summary>
public readonly struct ScopedBeatmaps
{
	private readonly UserPrivileges _privileges;

	internal ScopedBeatmaps(IActor? actor, TimeProvider clock)
	{
		_privileges = actor?.EffectivePrivileges(clock.GetUtcNow()) ?? UserPrivileges.None;
	}

	/// <summary>Tells whether the caller may see the set.</summary>
	public bool CanView(BeatmapsetRecord set)
	{
		return set.CanBeViewedBy(_privileges);
	}

	/// <summary>Tells whether the caller may see the beatmap.</summary>
	public bool CanView(BeatmapRecord map)
	{
		return map.CanBeViewedBy(_privileges);
	}

	/// <summary>Narrows a search to what the caller may see.</summary>
	public BeatmapQuery Limit(BeatmapQuery query)
	{
		return query.LimitTo(_privileges);
	}

	/// <summary>Tells whether the caller may use osu!direct.</summary>
	public bool MayUseDirect()
	{
		return BeatmapsetRecord.MayUseDirect(_privileges);
	}
}
