using Basil.Application.Beatmaps.Objects;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

public readonly struct ScopedBeatmapset
{
	public IActor Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedBeatmapset(Beatmapset beatmapset, IActor actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(beatmapset.Context.Clock.GetUtcNow());
		_context = beatmapset.Context;
		_setId = beatmapset.Record.Id;
		Beatmapset = beatmapset;
	}

	internal ScopedBeatmapset(BeatmapsetContext context, int setId, IActor actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(context.Clock.GetUtcNow());
		_context = context;
		_setId = setId;
		Beatmapset = null;
	}

	private readonly BeatmapsetContext _context;
	private readonly int _setId;

	public Beatmapset? Beatmapset { get; }

	private async Task<Beatmapset?> ResolveAsync()
	{
		if (Beatmapset is { } resolved) return resolved;

		return await _context.Repository.GetAsync(_setId) is { } record ? new Beatmapset(record, _context) : null;
	}

	public async Task<BeatmapsetOutcome> DeleteAsync()
	{
		if (Lacks(UserPrivileges.Tournament)) return BeatmapsetOutcome.Denied;

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.DeleteAsync();
	}

	public async Task<BeatmapsetOutcome> SetVisibleAsync(bool visible)
	{
		if (Lacks(UserPrivileges.Tournament)) return BeatmapsetOutcome.Denied;

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetVisibleAsync(visible);
	}

	public async Task<BeatmapsetOutcome> SetLockedAsync(bool locked)
	{
		if (Lacks(UserPrivileges.Tournament)) return BeatmapsetOutcome.Denied;

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetLockedAsync(locked);
	}

	public async Task<BeatmapsetOutcome> SetStatusAsync(BeatmapsetStatus status)
	{
		if (Lacks(UserPrivileges.Tournament)) return BeatmapsetOutcome.Denied;

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetStatusAsync(status);
	}
}