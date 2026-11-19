using Basil.Domain.Content;

namespace Basil.Application.Content.Storage;

public interface IMenuSeasonalsStorage
{
	Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default);

	Task<ContentReference> SaveAsync(string name, Stream content);

	Task<Stream?> OpenAsync(ContentReference background, CancellationToken ct = default);

	Task<ContentReference?> RenameAsync(ContentReference background, string newName);

	Task DeleteAsync(ContentReference background);
}