using Basil.Application.Storage.Contracts.Content;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the images of main-menu banners under their names.</summary>
internal sealed class FileMenuBannerStorage(DataPaths paths) : IMenuBannerStorage
{
	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(FileStorage.List(paths.MenuBanners));
	}

	/// <inheritdoc />
	public Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		return FileStorage.SaveAsync(SafePath.Combine(paths.MenuBanners, name), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(FileStorage.Open(SafePath.Combine(paths.MenuBanners, name)));
	}

	/// <inheritdoc />
	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		FileStorage.Delete(SafePath.Combine(paths.MenuBanners, name));
		return Task.CompletedTask;
	}
}
