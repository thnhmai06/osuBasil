using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Common.Infrastructure.Files;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>Stores seasonal menu background images as files in the data directory.</summary>
internal sealed class FileMenuSeasonalsStorage(DataPaths paths) : IMenuSeasonalsStorage
{
	private DataStorage Files => paths.MenuSeasonals;

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
	public Task<Stream?> OpenAsync(ContentReference background, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(background, out var name) ? Files.Open(name) : null);
	}

	/// <inheritdoc />
	public Task<ContentReference?> RenameAsync(ContentReference background, string newName,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult<ContentReference?>(
			Files.TryGetName(background, out var name) && Files.Move(name, newName)
				? Files.ReferenceOf(newName)
				: null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(ContentReference background, CancellationToken cancellationToken = default)
	{
		if (Files.TryGetName(background, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}