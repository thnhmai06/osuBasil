using Basil.Domain.Content;
using Basil.Shared.Structures.Cryptography;


namespace Basil.Application.Beatmaps.Persistence;

public abstract class BeatmapsetSource(string name)
{
	public string Name => name;
}

public abstract class BeatmapsetArchive(string name, ContentMetadata metadata) : BeatmapsetSource(name)
{
	public ContentMetadata Metadata => metadata;

	public abstract Task<Stream> OpenAsync(CancellationToken ct = default);

	public async Task<ContentIdentifier> FingerprintAsync(CancellationToken ct = default)
	{
		var stream = await OpenAsync(ct);
		await using (stream)
		{
			return new ContentIdentifier(metadata,
				await Md5Hash.ComputeAsync(stream, ct));
		}
	}
}

public abstract class BeatmapsetFolder(string name, int? id, bool exists, BeatmapsetArchive? archive)
	: BeatmapsetSource(name)
{
	public int? Id => id;

	public bool Exists => exists;

	public BeatmapsetArchive? Archive => archive;
}