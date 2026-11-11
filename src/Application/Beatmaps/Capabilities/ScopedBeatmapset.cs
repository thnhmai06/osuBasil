using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>A beatmapset as one actor may act on it.</summary>
public readonly struct ScopedBeatmapset
{
	/// <summary>Gets the actor this scope is for.</summary>
	public IActor Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Privileges { get; }

	/// <summary>Checks whether the actor holds the privilege an action needs.</summary>
	/// <param name="required">The privilege the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPrivileges? Require(UserPrivileges required)
	{
		return ScopedPrivileges.Require(Privileges, required);
	}

	/// <summary>Creates a scoped beatmapset for an actor.</summary>
	/// <param name="beatmapset">The beatmapset to scope.</param>
	/// <param name="actor">The actor acting on the beatmapset.</param>
	internal ScopedBeatmapset(Beatmapset beatmapset, IActor actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(beatmapset.Context.Clock.GetUtcNow());
		_context = beatmapset.Context;
		_setId = beatmapset.Record.Id;
		Beatmapset = beatmapset;
	}

	/// <summary>Creates a scoped beatmapset that is only referenced by its id.</summary>
	/// <param name="context">The context the beatmapset is read through.</param>
	/// <param name="setId">The id of the beatmapset the caller refers to.</param>
	/// <param name="actor">The actor acting on the beatmapset.</param>
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

	/// <summary>Gets the beatmapset this scope is over, or <see langword="null" /> when the scope only references it by id.</summary>
	public Beatmapset? Beatmapset { get; }

	/// <summary>Gets the beatmapset this scope is over; the stored beatmapset is read only now.</summary>
	/// <returns>The beatmapset, or <see langword="null" /> when no beatmapset has the id.</returns>
	private async Task<Beatmapset?> ResolveAsync()
	{
		if (Beatmapset is { } resolved) return resolved;

		return await _context.Repository.GetAsync(_setId) is { } record ? new Beatmapset(record, _context) : null;
	}

	/// <summary>Deletes this beatmapset and its files.</summary>
	/// <returns>The outcome of deleting the beatmapset; a locked set is not deleted.</returns>
	public async Task<Result<BeatmapsetOutcome>> DeleteAsync()
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.DeleteAsync();
	}

	/// <summary>Changes whether this beatmapset is shown in public listings.</summary>
	/// <param name="visible">Whether the beatmapset is visible.</param>
	/// <returns>The outcome of changing the beatmapset's visibility.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetVisibleAsync(bool visible)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetVisibleAsync(visible);
	}

	/// <summary>Changes whether this beatmapset is write-locked.</summary>
	/// <param name="locked">Whether the beatmapset is write-locked.</param>
	/// <returns>The outcome of changing the beatmapset's lock.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetLockedAsync(bool locked)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetLockedAsync(locked);
	}

	/// <summary>Changes the ranked status the beatmapset is reported with.</summary>
	/// <param name="status">The new status.</param>
	/// <returns>The outcome of changing the beatmapset's status.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetStatusAsync(BeatmapsetStatus status)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		if (await ResolveAsync() is not { } set) return BeatmapsetOutcome.NotFound;

		return await set.SetStatusAsync(status);
	}
}