using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores seasonal menu background images as files in the data directory.</summary>
internal sealed class FileMenuSeasonalsStorage(DataPaths paths) : IMenuSeasonalsStorage
{
	private DataStorage Files => paths.MenuSeasonals;

	/// <inheritdoc />
	public Task<IEnumerable<AssetReference>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IEnumerable<AssetReference>>(
			Files.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Files.ReferenceOf));
	}

	/// <inheritdoc />
	public async Task<AssetReference> SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		await Files.SaveAsync(name, content, cancellationToken);
		return Files.ReferenceOf(name);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(AssetReference background, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(background, out var name) ? Files.Open(name) : null);
	}

	/// <inheritdoc />
	public Task<AssetReference?> RenameAsync(AssetReference background, string newName, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<AssetReference?>(
			Files.TryGetName(background, out var name) && Files.Move(name, newName)
				? Files.ReferenceOf(newName)
				: null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(AssetReference background, CancellationToken cancellationToken = default)
	{
		if (Files.TryGetName(background, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}
