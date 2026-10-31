using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Content.Storage;

/// <summary>Stores player-uploaded screenshots as files in the data directory.</summary>
internal sealed class FileScreenshotStorage(DataPaths paths) : IScreenshotStorage
{
	private DataStorage Files => paths.Screenshots;

	/// <inheritdoc />
	public Task<IEnumerable<ContentReference>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(
			Files.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(Files.ReferenceOf));
	}

	/// <inheritdoc />
	public async Task<ContentReference> SaveAsync(Stream content, string extension,
		CancellationToken cancellationToken = default)
	{
		if (extension.Length is < 1 or > 8 || !extension.All(char.IsAsciiLetterOrDigit))
			throw new ArgumentException("The extension must be 1 to 8 ASCII letters or digits.", nameof(extension));

		var name = $"{Guid.CreateVersion7():D}.{extension.ToLowerInvariant()}";
		await Files.SaveAsync(name, content, cancellationToken);
		return Files.ReferenceOf(name);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(ContentReference screenshot, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.TryGetName(screenshot, out var name) ? Files.Open(name) : null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(ContentReference screenshot, CancellationToken cancellationToken = default)
	{
		if (Files.TryGetName(screenshot, out var name))
			Files.Delete(name);
		return Task.CompletedTask;
	}
}