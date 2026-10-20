using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>A beatmapset scoped to one connection and its authority snapshot.</summary>
/// <remarks>The snapshot is for one request or command; keeping it longer lets its rights outlive changes to permissions or restrictions.</remarks>
public sealed class ScopedBeatmapset
{
	private readonly DateTimeOffset _now;

	/// <summary>The beatmapset this scope is over.</summary>
	public Beatmapset Beatmapset { get; }

	/// <summary>The connection acting on the beatmapset.</summary>
	public Connection By { get; }

	/// <summary>Creates a scoped beatmapset for a connection.</summary>
	/// <param name="beatmapset">The beatmapset to scope.</param>
	/// <param name="by">The connection acting on the beatmapset.</param>
	internal ScopedBeatmapset(Beatmapset beatmapset, Connection by)
	{
		Beatmapset = beatmapset;
		By = by;
		_now = beatmapset.Context.Clock.GetUtcNow();
	}

	/// <summary>Deletes this beatmapset and its files.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the beatmapset.</returns>
	public async Task<BeatmapsetResult> DeleteAsync(CancellationToken ct = default)
	{
		if (Require() is { } denied) return denied;
		if (Beatmapset.Record.Locked) return BeatmapsetResult.Locked;

		await Beatmapset.Context.Storage.DeleteAsync(Beatmapset.Record, ct);
		await Beatmapset.ForgetAsync(ct);
		return BeatmapsetResult.Ok;
	}

	/// <summary>Changes whether this beatmapset is shown in public listings.</summary>
	/// <param name="visible">Whether the beatmapset is visible.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's visibility.</returns>
	public async Task<BeatmapsetResult> SetVisibleAsync(bool visible, CancellationToken ct = default)
	{
		if (Require() is { } denied) return denied;
		if (Beatmapset.Record.Visible == visible) return BeatmapsetResult.Ok;

		Beatmapset.Record.Visible = visible;
		await Beatmapset.Context.Repository.UpdateAsync(Beatmapset.Record, ct);
		return BeatmapsetResult.Ok;
	}

	/// <summary>Changes whether this beatmapset is write-locked.</summary>
	/// <param name="locked">Whether the beatmapset is write-locked.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of changing the beatmapset's lock.</returns>
	public async Task<BeatmapsetResult> SetLockedAsync(bool locked, CancellationToken ct = default)
	{
		if (Require() is { } denied) return denied;
		if (Beatmapset.Record.Locked == locked) return BeatmapsetResult.Ok;

		Beatmapset.Record.Locked = locked;
		await Beatmapset.Context.Repository.UpdateAsync(Beatmapset.Record, ct);
		return BeatmapsetResult.Ok;
	}

	private BeatmapsetResult? Require()
	{
		return By.Session.Check(Permissions.BeatmapsetManage, _now) switch
		{
			Access.NotGranted => BeatmapsetResult.NotAuthorized,
			Access.Suspended => BeatmapsetResult.Restricted,
			_ => null
		};
	}
}