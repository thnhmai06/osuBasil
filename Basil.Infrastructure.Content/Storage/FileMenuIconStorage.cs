using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Content.Storage;

internal sealed class FileMenuIconStorage(DataPaths paths) : IMenuIconStorage
{
	private DataStorage Files => paths.MenuIcon;

	public async Task<ContentReference> SaveAsync(string name, Stream content)
	{
		await Files.SaveAsync(name, content, CancellationToken.None);
		Files.DeleteAllExcept(name);
		return Files.ReferenceOf(name);
	}

	public Task<(ContentReference Reference, Stream Content)?> OpenAsync(CancellationToken ct = default)
	{
		var key = Files.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
		if (key is null)
			return Task.FromResult<(ContentReference Reference, Stream Content)?>(null);

		var stream = Files.Open(key);
		return Task.FromResult<(ContentReference Reference, Stream Content)?>(
			stream is null ? null : (Files.ReferenceOf(key), stream));
	}

	public Task DeleteAsync()
	{
		Files.DeleteAllExcept(null);
		return Task.CompletedTask;
	}
}