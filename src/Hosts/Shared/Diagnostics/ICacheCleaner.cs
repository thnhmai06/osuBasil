namespace Basil.Host.Shared.Diagnostics;

/// <summary>Empties the cache folder of derived files, which are made again when needed.</summary>
public interface ICacheCleaner
{
	/// <summary>Deletes the cached files.</summary>
	/// <returns>How many files were deleted.</returns>
	int Clear();
}