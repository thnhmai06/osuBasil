using Basil.Application.Abstractions.Storage;
using Basil.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage;

/// <inheritdoc cref="IResponseCache" />
public sealed class FileSystemResponseCache(IOptions<StorageOptions> options) : IResponseCache
{
	public async Task<byte[]?> GetAsync(string endpoint, string relativePath,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
	}

	public async Task PutAsync(string endpoint, string relativePath, byte[] content,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllBytesAsync(path, content, cancellationToken);
	}

	public Task DeleteAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		if (File.Exists(path)) File.Delete(path);
		return Task.CompletedTask;
	}

	private string PathFor(string endpoint, string relativePath)
	{
		return Path.Combine(options.Value.CachePath, endpoint, relativePath);
	}
}
