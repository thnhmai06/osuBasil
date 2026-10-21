using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Files;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>Stores menu banner images as files in the data directory.</summary>
internal sealed class FileMenuBannerStorage(DataPaths paths) : IMenuBannerStorage
{
	private DataStorage Files => paths.MenuBanners;

	/// <inheritdoc />
	public Task<IEnumerable<ContentReference>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(
			Files.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Files.ReferenceOf));
	}

	/// <inheritdoc />
	public async Task<ContentReference> SaveAsync(string name, Stream content,
		CancellationToken cancellationToken = default)
	{
		await Files.SaveAsync(name, content, cancellationToken);
		return Files.ReferenceOf(name);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(ContentReference image, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(image, out var name) ? Files.Open(name) : null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(ContentReference image, CancellationToken cancellationToken = default)
	{
		if (Files.TryGetName(image, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}