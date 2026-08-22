using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Resolves an ImageSharp.Web image request directly from an absolute file path.
/// </summary>
/// <remarks>
///     Used where the resolved path isn't known until request time (e.g. the menu icon, whose file
///     path lives in a database setting), so a <see cref="Microsoft.Extensions.FileProviders.PhysicalFileProvider" />
///     rooted ahead of time doesn't fit.
/// </remarks>
/// <param name="file">The file to serve.</param>
public sealed class PhysicalFileImageResolver(FileInfo file) : IImageResolver
{
	/// <inheritdoc />
	public Task<ImageMetadata> GetMetaDataAsync()
	{
		return Task.FromResult(new ImageMetadata(file.LastWriteTimeUtc, file.Length));
	}

	/// <inheritdoc />
	public Task<Stream> OpenReadAsync()
	{
		return Task.FromResult<Stream>(file.OpenRead());
	}
}