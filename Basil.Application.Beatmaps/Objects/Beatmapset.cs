using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Objects;

public sealed class Beatmapset
{
	internal Beatmapset(BeatmapsetRecord record, BeatmapsetContext context)
	{
		Record = record;
		Context = context;
	}

	public BeatmapsetRecord Record { get; }

	internal BeatmapsetContext Context { get; }

	public ScopedBeatmapset ScopeTo(IActor actor)
	{
		return new ScopedBeatmapset(this, actor);
	}

	internal async Task ForgetAsync()
	{
		await Context.Repository.CommitAsync(new BeatmapsetChanges([], [Record]));
		Context.Events.Publish(new BeatmapsetDeleted(Record));
	}

	internal async Task<BeatmapsetEditResult> DeleteAsync()
	{
		if (Record.Locked) return BeatmapsetEditResult.Locked;

		await Context.Storage.DeleteAsync(Record);
		await ForgetAsync();
		return BeatmapsetEditResult.Ok;
	}

	internal async Task<BeatmapsetEditResult> SetVisibleAsync(bool visible)
	{
		if (Record.Visible == visible) return BeatmapsetEditResult.Ok;

		Record.Visible = visible;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetEditResult.Ok;
	}

	internal async Task<BeatmapsetEditResult> SetLockedAsync(bool locked)
	{
		if (Record.Locked == locked) return BeatmapsetEditResult.Ok;

		Record.Locked = locked;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetEditResult.Ok;
	}

	internal async Task<BeatmapsetEditResult> SetStatusAsync(BeatmapsetStatus status)
	{
		if (Record.Status == status) return BeatmapsetEditResult.Ok;

		Record.Status = status;
		await Context.Repository.UpdateAsync(Record);
		return BeatmapsetEditResult.Ok;
	}
}