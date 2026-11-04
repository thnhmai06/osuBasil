using System.Buffers;
using Basil.Application.Common.Storage;

namespace Basil.Infrastructure.Common.Storage;

/// <summary>Spools uploads into temporary files under the data directory; the files are named by no part of the upload.</summary>
public sealed class FileUploadSpool(DataPaths paths, TimeProvider time) : IUploadSpool
{
	/// <inheritdoc />
	public async Task<ISpooledUpload> SpoolAsync(Stream content, long maxBytes,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(paths.Spool);
		var path = Path.Combine(paths.Spool, Guid.NewGuid().ToString("N") + ".tmp");
		var file = new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			Options = FileOptions.Asynchronous,
			BufferSize = 4096
		});
		var buffer = ArrayPool<byte>.Shared.Rent(81920);
		try
		{
			long total = 0;
			while (total <= maxBytes)
			{
				var allowance = (int)Math.Min(buffer.Length, maxBytes + 1 - total);
				var read = await content.ReadAsync(buffer.AsMemory(0, allowance), cancellationToken);
				if (read == 0) break;
				await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				total += read;
			}

			file.Position = 0;
			return new Spooled(file, path, paths, time, total > maxBytes);
		}
		catch
		{
			await file.DisposeAsync();
			File.Delete(path);
			throw;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private sealed class Spooled(FileStream file, string path, DataPaths paths, TimeProvider time, bool tooLarge)
		: ISpooledUpload
	{
		public Stream Content => file;

		public bool TooLarge { get; } = tooLarge;

		public async Task QuarantineAsync(CancellationToken cancellationToken = default)
		{
			await file.DisposeAsync();
			Quarantine.Keep(paths, path, time);
		}

		public async ValueTask DisposeAsync()
		{
			await file.DisposeAsync();
			if (File.Exists(path)) File.Delete(path);
		}
	}
}
