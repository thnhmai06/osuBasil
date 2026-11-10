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
	public UserPrivileges Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPrivileges? Require(UserPrivileges required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a scoped beatmapset for an actor.</summary>
	/// <param name="beatmapset">The beatmapset to scope.</param>
	/// <param name="actor">The actor acting on the beatmapset.</param>
	internal ScopedBeatmapset(Beatmapset beatmapset, IActor actor)
	{
		Actor = actor;
		Rights = actor.EffectivePrivileges(beatmapset.Context.Clock.GetUtcNow());
		Beatmapset = beatmapset;
	}

	/// <summary>Gets the beatmapset this scope is over.</summary>
	public Beatmapset Beatmapset { get; }

	/// <summary>Deletes this beatmapset and its files.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the beatmapset; a locked set is not deleted.</returns>
	public async Task<Result<BeatmapsetOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		return await Beatmapset.DeleteAsync(cancellationToken);
	}

	/// <summary>Changes whether this beatmapset is shown in public listings.</summary>
	/// <param name="visible">Whether the beatmapset is visible.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's visibility.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetVisibleAsync(bool visible,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		return await Beatmapset.SetVisibleAsync(visible, cancellationToken);
	}

	/// <summary>Changes whether this beatmapset is write-locked.</summary>
	/// <param name="locked">Whether the beatmapset is write-locked.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's lock.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetLockedAsync(bool locked,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		return await Beatmapset.SetLockedAsync(locked, cancellationToken);
	}

	/// <summary>Changes the ranked status the beatmapset is reported with.</summary>
	/// <param name="status">The new status.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's status.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetStatusAsync(BeatmapsetStatus status,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Tournament) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);

		return await Beatmapset.SetStatusAsync(status, cancellationToken);
	}
}