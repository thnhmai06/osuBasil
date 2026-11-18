using Basil.Domain.Content;

namespace Basil.Application.Content.Storage;

public interface IMenuIconStorage
{
	Task<ContentReference> SaveAsync(string name, Stream content);

	Task<(ContentReference Reference, Stream Content)?> OpenAsync(CancellationToken ct = default);

	Task DeleteAsync();
}