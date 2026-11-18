using Basil.Domain.Content;

namespace Basil.Application.Content.Storage;

public interface IScreenshotStorage
{
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default);

	Task<ContentReference> SaveAsync(Stream content, string extension);

	Task<Stream?> OpenAsync(ContentReference screenshot, CancellationToken ct = default);

	Task DeleteAsync(ContentReference screenshot);
}