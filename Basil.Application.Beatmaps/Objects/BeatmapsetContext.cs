using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Events;

namespace Basil.Application.Beatmaps.Objects;

internal sealed record BeatmapsetContext(
	TimeProvider Clock,
	IEventPublisher<BeatmapsetEvent> Events,
	IBeatmapsetRepository Repository,
	IBeatmapsetStorage Storage);