namespace Basil.Infrastructure.Shared.Storage;

public static class Quarantine
{
	private static readonly TimeSpan Kept = TimeSpan.FromDays(7);

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