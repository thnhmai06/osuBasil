using System.Security.Cryptography;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Host.Rest.Images;

/// <summary>
///     Base of the image providers: every image the server serves goes through the same pipeline of resizing, file
///     cache and HTTP caching. A provider never reads the Bearer token, because every image is public.
/// </summary>
public abstract class StreamImageProvider : IImageProvider
{
	/// <summary>Initializes the provider for the images of one sub-domain.</summary>
	/// <param name="label">The first label of the host name, such as <c>a</c> or <c>assets</c>.</param>
	/// <param name="path">Tells whether a request path is for this provider.</param>
	protected StreamImageProvider(string label, Func<PathString, bool> path)
	{
		Match = context =>
			context.Request.Host.Host.StartsWith(label + ".", StringComparison.OrdinalIgnoreCase) &&
			path(context.Request.Path);
	}

	/// <inheritdoc />
	public ProcessingBehavior ProcessingBehavior => ProcessingBehavior.All;

	/// <inheritdoc />
	public Func<HttpContext, bool> Match { get; set; }

	/// <inheritdoc />
	public bool IsValidRequest(HttpContext context)
	{
		return true;
	}

	/// <inheritdoc />
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

	/// <summary>Opens the image for a request path.</summary>
	/// <param name="path">The request path.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image, or <see langword="null" /> when there is none or it must not be seen.</returns>
	protected abstract Task<Stream?> OpenAsync(PathString path, CancellationToken cancellationToken);

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