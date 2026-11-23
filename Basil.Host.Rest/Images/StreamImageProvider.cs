using System.Security.Cryptography;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Host.Rest.Images;

public abstract class StreamImageProvider : IImageProvider
{
	protected StreamImageProvider(string label, Func<PathString, bool> path)
	{
		Match = context =>
			context.Request.Host.Host.StartsWith(label + ".", StringComparison.OrdinalIgnoreCase) &&
			path(context.Request.Path);
	}

	public ProcessingBehavior ProcessingBehavior => ProcessingBehavior.All;

	public Func<HttpContext, bool> Match { get; set; }

	public bool IsValidRequest(HttpContext context)
	{
		return true;
	}

	public async Task<IImageResolver?> GetAsync(HttpContext context)
	{
		var stream = await OpenAsync(context.Request.Path, context.RequestAborted);
		if (stream is null) return null;

		await using (stream)
		{
			if (stream is FileStream file)
			{
				var info = new FileInfo(file.Name);
				return new FileResolver(file.Name, info.LastWriteTimeUtc, info.Length);
			}

			var memory = new MemoryStream();
			await stream.CopyToAsync(memory, context.RequestAborted);
			return new BufferedResolver(memory.ToArray());
		}
	}

	protected abstract Task<Stream?> OpenAsync(PathString path, CancellationToken ct);

	// The file is reopened by path only when ImageSharp misses its cache, so a cached image never reads it.
	private sealed class FileResolver(string path, DateTime lastWriteUtc, long length) : IImageResolver
	{
		public Task<ImageMetadata> GetMetaDataAsync()
		{
			return Task.FromResult(new ImageMetadata(lastWriteUtc, length));
		}

		public Task<Stream> OpenReadAsync()
		{
			return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
				FileOptions.Asynchronous | FileOptions.SequentialScan));
		}
	}

	private sealed class BufferedResolver(byte[] content) : IImageResolver
	{
		public Task<ImageMetadata> GetMetaDataAsync()
		{
			// shortcut: a non-file stream gives no modification time, so the content's hash stands in for it and
			// invalidates the cache when the image changes; only the embedded default avatar takes this path.
			var hash = MD5.HashData(content);
			var seconds = BitConverter.ToUInt32(hash, 0);
			return Task.FromResult(new ImageMetadata(DateTime.UnixEpoch.AddSeconds(seconds), content.Length));
		}

		public Task<Stream> OpenReadAsync()
		{
			return Task.FromResult<Stream>(new MemoryStream(content, false));
		}
	}
}