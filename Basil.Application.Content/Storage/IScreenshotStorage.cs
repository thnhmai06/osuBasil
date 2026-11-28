using Basil.Domain.Content;

namespace Basil.Application.Content.Storage;

public interface IScreenshotStorage
{
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default);

	/// <summary>Stores a picture once <paramref name="accept" />, shown the stored bytes, returns its file extension; when it throws, nothing is stored.</summary>
	Task<ContentReference> SaveAsync(Stream content, Func<Stream, CancellationToken, Task<string>> accept,
		CancellationToken ct = default);

	Task<Stream?> OpenAsync(ContentReference screenshot, CancellationToken ct = default);

	Task DeleteAsync(ContentReference screenshot);
}