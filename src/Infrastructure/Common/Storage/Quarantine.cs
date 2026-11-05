namespace Basil.Infrastructure.Common.Storage;

/// <summary>Keeps the uploads that failed validation, for the operator to look at, and forgets them after a week.</summary>
public static class Quarantine
{
	private static readonly TimeSpan Kept = TimeSpan.FromDays(7);

	/// <summary>Moves a file into the quarantine under a name of its own and deletes the quarantined files that are too old.</summary>
	/// <param name="paths">The locations of the data directory.</param>
	/// <param name="file">The absolute path of the file to keep; no part of the quarantined name comes from it.</param>
	/// <param name="time">The clock that tells which quarantined files are too old.</param>
	public static void Keep(DataPaths paths, string file, TimeProvider time)
	{
		Directory.CreateDirectory(paths.Quarantine);
		File.Move(file, Path.Combine(paths.Quarantine, Guid.NewGuid().ToString("N")));

		var expired = time.GetUtcNow().UtcDateTime - Kept;
		foreach (var old in Directory.EnumerateFiles(paths.Quarantine))
			if (File.GetLastWriteTimeUtc(old) < expired)
				File.Delete(old);
	}
}