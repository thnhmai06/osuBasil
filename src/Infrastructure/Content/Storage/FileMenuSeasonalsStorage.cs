using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Content.Storage;

internal sealed class FileMenuSeasonalsStorage(DataPaths paths) : IMenuSeasonalsStorage
{
	private DataStorage Files => paths.MenuSeasonals;

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

	public Task<Stream?> OpenAsync(ContentReference background, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(background, out var name) ? Files.Open(name) : null);
	}

	public Task<ContentReference?> RenameAsync(ContentReference background, string newName)
	{
		return Task.FromResult<ContentReference?>(
			Files.TryGetName(background, out var name) && Files.Move(name, newName)
				? Files.ReferenceOf(newName)
				: null);
	}

	public Task DeleteAsync(ContentReference background)
	{
		if (Files.TryGetName(background, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}