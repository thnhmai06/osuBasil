using Basil.Domain.Content;

namespace Basil.Application.Content.Storage;

public interface IMenuBannerStorage
{
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default);

	Task<ContentReference> SaveAsync(string name, Stream content);

	Task<Stream?> OpenAsync(ContentReference image, CancellationToken ct = default);

	Task DeleteAsync(ContentReference image);
}