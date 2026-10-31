using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Content.Storage;

/// <summary>Stores the main-menu icon image as a file in the data directory.</summary>
internal sealed class FileMenuIconStorage(DataPaths paths) : IMenuIconStorage
{
	private DataStorage Files => paths.MenuIcon;

	/// <inheritdoc />
	public async Task<ContentReference> SaveAsync(string name, Stream content,
		CancellationToken cancellationToken = default)
	{
		await Files.SaveAsync(name, content, cancellationToken);
		Files.DeleteAllExcept(name);
		return Files.ReferenceOf(name);
	}

	/// <inheritdoc />
	public Task<(ContentReference Reference, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default)
	{
		var key = Files.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
		if (key is null)
			return Task.FromResult<(ContentReference Reference, Stream Content)?>(null);

		var stream = Files.Open(key);
		return Task.FromResult<(ContentReference Reference, Stream Content)?>(
			stream is null ? null : (Files.ReferenceOf(key), stream));
	}

	/// <inheritdoc />
	public Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		Files.DeleteAllExcept(null);
		return Task.CompletedTask;
	}
}