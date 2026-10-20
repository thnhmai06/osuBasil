using Basil.Application.Storage.Contracts.Beatmaps;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>The ports through which a beatmapset performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating permissions.</param>
/// <param name="Events">The event sink for announcing beatmapset changes.</param>
/// <param name="Repository">The stored beatmapset records.</param>
/// <param name="Storage">The files that belong to each beatmapset.</param>
internal sealed record BeatmapsetContext(
	TimeProvider Clock,
	IBeatmapsetEventSink Events,
	IBeatmapsetRepository Repository,
	IBeatmapsetStorage Storage);

/// <summary>Emits beatmapset events to the beatmapset event stream.</summary>
internal interface IBeatmapsetEventSink
{
	/// <summary>Emits a beatmapset event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(BeatmapsetEvent @event);
}