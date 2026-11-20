using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Objects;

public sealed class Beatmapsets
{
	private readonly BeatmapsetContext _context;
	private readonly IBeatmapsetImporter _importer;
	private readonly IBeatmapsetScanner _scanner;

	internal Beatmapsets(BeatmapsetContext context, IBeatmapsetImporter importer, IBeatmapsetScanner scanner)
	{
		_context = context;
		_importer = importer;
		_scanner = scanner;
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
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));
		return result;
	}

	public async Task<BeatmapsetScanResult> ScanAsync(CancellationToken ct = default)
	{
		var outcome = await _scanner.ScanAsync(ct);
		Publish(outcome);
		return outcome.Result;
	}

	public async Task<BeatmapsetScanResult> ScanAsync(BeatmapsetSource entry, CancellationToken ct = default)
	{
		var outcome = await _scanner.ScanAsync(entry, ct);
		Publish(outcome);
		return outcome.Result;
	}

	private void Publish(BeatmapsetScanOutcome outcome)
	{
		foreach (var imported in outcome.Imported)
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));

		foreach (var deleted in outcome.Deleted)
			_context.Events.Publish(new BeatmapsetDeleted(deleted));
	}
}