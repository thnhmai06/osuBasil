using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the main-menu icon image as a file in the data directory.</summary>
internal sealed class FileMenuIconStorage(DataPaths paths) : IMenuIconStorage
{
	private DataStorage Files => paths.MenuIcon;

	/// <inheritdoc />
	public async Task<AssetReference> SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		await Files.SaveAsync(name, content, cancellationToken);
		Files.DeleteAllExcept(name);
		return Files.ReferenceOf(name);
	}

	/// <inheritdoc />
	public Task<(AssetReference Reference, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default)
	{
		var key = Files.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
		if (key is null)
			return Task.FromResult<(AssetReference Reference, Stream Content)?>(null);

		var stream = Files.Open(key);
		if (stream is null)
			return Task.FromResult<(AssetReference Reference, Stream Content)?>(null);

		return Task.FromResult<(AssetReference Reference, Stream Content)?>((Files.ReferenceOf(key), stream));
	}

	/// <inheritdoc />
	public Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		Files.DeleteAllExcept(null);
		return Task.CompletedTask;
	}
}
