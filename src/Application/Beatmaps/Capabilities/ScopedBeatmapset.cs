using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>A beatmapset as one actor may act on it.</summary>
public sealed class ScopedBeatmapset : Scoped<UserPermissions>
{
	/// <summary>Creates a scoped beatmapset for an actor.</summary>
	/// <param name="beatmapset">The beatmapset to scope.</param>
	/// <param name="by">The actor acting on the beatmapset.</param>
	internal ScopedBeatmapset(Beatmapset beatmapset, IActor by) : base(by,
		by.EffectivePermissions(beatmapset.Context.Clock.GetUtcNow()))
	{
		Beatmapset = beatmapset;
	}

	/// <summary>Gets the beatmapset this scope is over.</summary>
	public Beatmapset Beatmapset { get; }

	/// <summary>Deletes this beatmapset and its files.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the beatmapset; a locked set is not deleted.</returns>
	public async Task<Result<BeatmapsetOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.BeatmapsetManage) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);
		if (Beatmapset.Record.Locked) return BeatmapsetOutcome.Locked;

		await Beatmapset.Context.Storage.DeleteAsync(Beatmapset.Record, cancellationToken);
		await Beatmapset.ForgetAsync(cancellationToken);
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes whether this beatmapset is shown in public listings.</summary>
	/// <param name="visible">Whether the beatmapset is visible.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's visibility.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetVisibleAsync(bool visible,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.BeatmapsetManage) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);
		if (Beatmapset.Record.Visible == visible) return BeatmapsetOutcome.Ok;

		Beatmapset.Record.Visible = visible;
		await Beatmapset.Context.Repository.UpdateAsync(Beatmapset.Record, cancellationToken);
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes whether this beatmapset is write-locked.</summary>
	/// <param name="locked">Whether the beatmapset is write-locked.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's lock.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetLockedAsync(bool locked,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.BeatmapsetManage) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);
		if (Beatmapset.Record.Locked == locked) return BeatmapsetOutcome.Ok;

		Beatmapset.Record.Locked = locked;
		await Beatmapset.Context.Repository.UpdateAsync(Beatmapset.Record, cancellationToken);
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes the ranked status the beatmapset is reported with.</summary>
	/// <param name="status">The new status.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's status.</returns>
	public async Task<Result<BeatmapsetOutcome>> SetStatusAsync(BeatmapsetStatus status,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.BeatmapsetManage) is { } missing) return Result<BeatmapsetOutcome>.Denied(missing);
		if (Beatmapset.Record.Status == status) return BeatmapsetOutcome.Ok;

		Beatmapset.Record.Status = status;
		await Beatmapset.Context.Repository.UpdateAsync(Beatmapset.Record, cancellationToken);
		return BeatmapsetOutcome.Ok;
	}
}