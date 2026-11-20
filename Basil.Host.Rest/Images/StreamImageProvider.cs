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
			var memory = new MemoryStream();
			await stream.CopyToAsync(memory, context.RequestAborted);
			return new BufferedResolver(memory.ToArray());
		}
	}

	protected abstract Task<Stream?> OpenAsync(PathString path, CancellationToken ct);

	private sealed class BufferedResolver(byte[] content) : IImageResolver
	{
		public Task<ImageMetadata> GetMetaDataAsync()
		{
			// shortcut: the storages give no modification time, so the content's hash stands in for it and
			// invalidates the cache when the image changes; give the storages a time if images grow large.
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