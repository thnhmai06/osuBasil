using Basil.Domain.Cryptography;

namespace Basil.Application.Beatmaps.Persistence;

public abstract class BeatmapsetContentFile(string name, long length)
{
	public string Name => name;

	public long Length => length;

	public abstract Task<Stream> OpenAsync(CancellationToken ct = default);

	public async Task<Md5> HashAsync(CancellationToken ct = default)
	{
		var stream = await OpenAsync(ct);
		await using (stream)
		{
			return await Md5.ComputeAsync(stream, ct);
		}
	}
}