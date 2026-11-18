using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Content.Storage;

internal sealed class FileMenuBannerStorage(DataPaths paths) : IMenuBannerStorage
{
	private DataStorage Files => paths.MenuBanners;

	public Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default)
	{
		return Task.FromResult(
			Files.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Files.ReferenceOf));
	}

	public async Task<ContentReference> SaveAsync(string name, Stream content)
	{
		await Files.SaveAsync(name, content, CancellationToken.None);
		return Files.ReferenceOf(name);
	}

	public Task<Stream?> OpenAsync(ContentReference image, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(image, out var name) ? Files.Open(name) : null);
	}

	public Task DeleteAsync(ContentReference image)
	{
		if (Files.TryGetName(image, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}