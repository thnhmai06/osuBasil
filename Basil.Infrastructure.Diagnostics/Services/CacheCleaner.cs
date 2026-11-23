using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Diagnostics.Services;

internal sealed class CacheCleaner(DataPaths paths) : ICacheCleaner
{
	public int Clear()
	{
		var directory = paths.Cache.Path;
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