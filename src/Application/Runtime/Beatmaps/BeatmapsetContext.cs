using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Runtime.Events;

namespace Basil.Application.Runtime.Beatmaps;

/// <summary>The ports through which a beatmapset performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating permissions.</param>
/// <param name="Events">The publisher for beatmapset events.</param>
/// <param name="Repository">The stored beatmapset records.</param>
/// <param name="Storage">The files that belong to each beatmapset.</param>
internal sealed record BeatmapsetContext(
	TimeProvider Clock,
	IEventPublisher<BeatmapsetEvent> Events,
	IBeatmapsetRepository Repository,
	IBeatmapsetStorage Storage);