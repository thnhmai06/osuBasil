using Basil.Application.Storage.Contracts.Beatmaps;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>A file of a beatmapset read from a file on disk.</summary>
internal sealed class DiskBeatmapsetFile(FileInfo file, string name) : BeatmapsetFile(name, file.Length)
{
	/// <inheritdoc />
	public override Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<Stream>(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
			81920, FileOptions.Asynchronous));
	}
}
