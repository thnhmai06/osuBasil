using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Objects;

/// <summary>Owns the state changes of one beatmapset.</summary>
public sealed class Beatmapset
{
	/// <summary>Creates the beatmapset object for a stored set.</summary>
	/// <param name="record">The set to represent.</param>
	/// <param name="context">The runtime context for beatmapset behaviour.</param>
	internal Beatmapset(BeatmapsetRecord record, BeatmapsetContext context)
	{
		Record = record;
		Context = context;
	}

	/// <summary>Gets the stored set this object represents.</summary>
	public BeatmapsetRecord Record { get; }

	/// <summary>The runtime context for beatmapset behaviour.</summary>
	internal BeatmapsetContext Context { get; }

	/// <summary>Scopes this beatmapset's behaviour to an actor and its authority.</summary>
	/// <param name="actor">The actor acting on the beatmapset.</param>
	/// <returns>A short-lived scope for this beatmapset and actor.</returns>
	public ScopedBeatmapset ScopeTo(IActor actor)
	{
		return new ScopedBeatmapset(this, actor);
	}

	/// <summary>Forgets a stored beatmapset, leaving the files of its folder as they are.</summary>
	internal async Task ForgetAsync()
	{
		await Context.Repository.CommitAsync(new BeatmapsetChanges([], [Record]));
		Context.Events.Publish(new BeatmapsetDeleted(Record));
	}

	/// <summary>Deletes this beatmapset and its files.</summary>
	/// <returns>Locked when the beatmapset is write-locked, which is then not deleted; otherwise Ok.</returns>
	internal async Task<BeatmapsetOutcome> DeleteAsync()
	{
		if (Record.Locked) return BeatmapsetOutcome.Locked;

		await Context.Storage.DeleteAsync(Record);
		await ForgetAsync();
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes whether this beatmapset is shown in public listings.</summary>
	/// <param name="visible">Whether the beatmapset is visible.</param>
	/// <returns>Ok.</returns>
	internal async Task<BeatmapsetOutcome> SetVisibleAsync(bool visible)
	{
		if (Record.Visible == visible) return BeatmapsetOutcome.Ok;

		Record.Visible = visible;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes whether this beatmapset is write-locked.</summary>
	/// <param name="locked">Whether the beatmapset is write-locked.</param>
	/// <returns>Ok.</returns>
	internal async Task<BeatmapsetOutcome> SetLockedAsync(bool locked)
	{
		if (Record.Locked == locked) return BeatmapsetOutcome.Ok;

		Record.Locked = locked;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetOutcome.Ok;
	}

	/// <summary>Changes the ranked status the beatmapset is reported with.</summary>
	/// <param name="status">The new status.</param>
	/// <returns>Ok.</returns>
	internal async Task<BeatmapsetOutcome> SetStatusAsync(BeatmapsetStatus status)
	{
		if (Record.Status == status) return BeatmapsetOutcome.Ok;

		Record.Status = status;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetOutcome.Ok;
	}
}