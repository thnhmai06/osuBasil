using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Diagnostics.Services;

internal sealed class CacheCleaner(IOptions<StorageOptions> storage) : ICacheCleaner
{
	public int Clear()
	{
		var directory = Path.Combine(storage.Value.Location, "Cache");
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