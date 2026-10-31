using Basil.Host.Shared.Diagnostics;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Host;

/// <summary>Empties <c>Data/Cache</c>, whose files are derived and made again when needed.</summary>
internal sealed class CacheCleaner(IOptions<StorageOptions> storage) : ICacheCleaner
{
	/// <inheritdoc />
	public int Clear()
	{
		var directory = Path.Combine(storage.Value.DataDirectory, "Cache");
		if (!Directory.Exists(directory)) return 0;

		var deleted = 0;
		foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			File.Delete(file);
			deleted++;
		}

		return deleted;
	}
}