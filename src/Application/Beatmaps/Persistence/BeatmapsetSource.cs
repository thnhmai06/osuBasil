using Basil.Domain.Content;
using Basil.Domain.Cryptography;

namespace Basil.Application.Beatmaps.Persistence;

public abstract class BeatmapsetSource(string name)
{
	public string Name => name;
}

public abstract class BeatmapsetArchive(string name, ContentStamp stamp) : BeatmapsetSource(name)
{
	public ContentStamp Stamp => stamp;

	public abstract Task<Stream> OpenAsync(CancellationToken ct = default);

	public async Task<ContentFingerprint> FingerprintAsync(CancellationToken ct = default)
	{
		var stream = await OpenAsync(ct);
		await using (stream)
		{
			return new ContentFingerprint(stamp,
				await Md5.ComputeAsync(stream, ct));
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