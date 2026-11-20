using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Ports;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Waiting;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Objects;

public sealed class Beatmapsets
{
	private readonly BeatmapsetContext _context;
	private readonly IBeatmapsetImporter _importer;
	private readonly IBeatmapsetScanner _scanner;
	private readonly IBeatmapRepository _beatmaps;
	private readonly ISender _sender;
	private readonly ISettingsRepository _settings;
	private readonly Pending<int, BeatmapAnalysis?> _pending;

	internal Beatmapsets(BeatmapsetContext context, IBeatmapsetImporter importer, IBeatmapsetScanner scanner,
		IBeatmapRepository beatmaps, ISender sender, ISettingsRepository settings)
	{
		_context = context;
		_importer = importer;
		_scanner = scanner;
		_beatmaps = beatmaps;
		_sender = sender;
		_settings = settings;
		_pending = new Pending<int, BeatmapAnalysis?>(context.Clock);
	}

	public ScopedBeatmapset ScopeTo(IActor actor, int setId)
	{
		return new ScopedBeatmapset(_context, setId, actor);
	}

	public Beatmapset Of(BeatmapsetRecord set)
	{
		return new Beatmapset(set, _context);
	}

	public async Task<BeatmapsetImportResult> ImportAsync(Stream archive, bool replaceExisting = true,
		CancellationToken ct = default)
	{
		var (result, imported) = await _importer.ImportAsync(archive, replaceExisting, ct);
		if (imported is not null)
		{
			await CalculateAsync(imported.Beatmaps, ct);
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));
		}

		return result;
	}

	public async Task<BeatmapsetScanResult> ScanAsync(CancellationToken ct = default)
	{
		var outcome = await _scanner.ScanAsync(ct);
		await PublishAsync(outcome, ct);
		return outcome.Result;
	}

	public async Task<BeatmapsetScanResult> ScanAsync(BeatmapsetSource entry, CancellationToken ct = default)
	{
		var outcome = await _scanner.ScanAsync(entry, ct);
		await PublishAsync(outcome, ct);
		return outcome.Result;
	}

	/// <summary>Stores the difficulty a calculation reported for a beatmap, and hands it to the import waiting for it.</summary>
	internal async Task CalculatedAsync(int beatmapId, BeatmapAnalysis? analysis)
	{
		if (analysis is not null && await _beatmaps.GetAsync(beatmapId) is { } beatmap)
		{
			beatmap.Metadata = WithAnalysis(beatmap.Metadata, analysis);
			await _beatmaps.UpdateAsync(beatmap);
		}

		_pending.Complete(beatmapId, analysis);
	}

	private async Task PublishAsync(BeatmapsetScanOutcome outcome, CancellationToken ct)
	{
		foreach (var imported in outcome.Imported)
		{
			await CalculateAsync(imported.Beatmaps, ct);
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));
		}

		foreach (var deleted in outcome.Deleted)
			_context.Events.Publish(new BeatmapsetDeleted(deleted));
	}

	/// <summary>Asks for the difficulty of every beatmap that has none and waits, up to one deadline for the whole set, for the answers.</summary>
	private async Task CalculateAsync(IReadOnlyList<BeatmapRecord> beatmaps, CancellationToken ct)
	{
		var missing = beatmaps.Where(beatmap => beatmap.Metadata.Difficulty is null).ToList();
		if (missing.Count == 0) return;

		// The waiters exist before the requests go out, so no answer can arrive unseen.
		var waiting = missing.Where(beatmap => _pending.Register(beatmap.Id)).ToList();
		try
		{
			foreach (var beatmap in missing)
				await _sender.Send(new CalculateBeatmap(beatmap.Id, beatmap.Hash, beatmap.Metadata.Mode), ct);

			var end = _context.Clock.GetUtcNow() + (await _settings.GetAsync(ct)).Beatmaps.BeatmapWait;
			foreach (var beatmap in waiting)
			{
				var left = end - _context.Clock.GetUtcNow();
				var (arrived, analysis) = await _pending.WaitAsync(beatmap.Id,
					left > TimeSpan.Zero ? left : TimeSpan.Zero, ct);
				// The answer is already stored; this only brings the record the caller holds up to date.
				if (arrived && analysis is not null) beatmap.Metadata = WithAnalysis(beatmap.Metadata, analysis);
			}
		}
		finally
		{
			foreach (var beatmap in waiting) _pending.Discard(beatmap.Id);
		}
	}

	private static BeatmapMetadata WithAnalysis(BeatmapMetadata metadata, BeatmapAnalysis analysis)
	{
		return new BeatmapMetadata
		{
			VersionName = metadata.VersionName, Mode = metadata.Mode, Difficulty = analysis.Difficulty,
			ObjectCount = analysis.ObjectCount
		};
	}
}