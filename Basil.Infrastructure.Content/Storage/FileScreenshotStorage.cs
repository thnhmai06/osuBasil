using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Content.Storage;

internal sealed class FileScreenshotStorage(DataPaths paths) : IScreenshotStorage
{
	private DataStorage Files => paths.Screenshots;

	public Task<IEnumerable<ContentReference>> ListAsync(CancellationToken ct = default)
	{
		return Task.FromResult(
			Files.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Files.ReferenceOf));
	}

	public async Task<ContentReference> SaveAsync(Stream content,
		Func<Stream, CancellationToken, Task<string>> accept, CancellationToken ct = default)
	{
		var name = await Files.SaveCheckedAsync(content, async (stored, token) =>
		{
			var extension = await accept(stored, token);
			if (extension.Length is < 1 or > 8 || !extension.All(char.IsAsciiLetterOrDigit))
				throw new ArgumentException("The extension must be 1 to 8 ASCII letters or digits.", nameof(extension));

			return $"{Guid.CreateVersion7():D}.{extension.ToLowerInvariant()}";
		}, ct);
		return Files.ReferenceOf(name);
	}

	public Task<Stream?> OpenAsync(ContentReference screenshot, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(screenshot, out var name) ? Files.Open(name) : null);
	}

	public Task DeleteAsync(ContentReference screenshot)
	{
		if (Files.TryGetName(screenshot, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}