namespace Basil.LoadTests.Hosting;

/// <summary>
///     Snapshots or restores a locally owned server's SQLite database
///     (<c>Data/Basil.db</c> + <c>-wal</c>/<c>-shm</c>), anchored to that server's own working
///     directory since the path resolves against <c>AppContext.BaseDirectory</c>, not the caller's CWD.
///     Only called while the server process is stopped, so the files are never touched mid-write.
/// </summary>
public static class ServerDatabase
{
	private static readonly string[] Suffixes = ["", "-wal", "-shm"];

	/// <summary>
	///     Restores <paramref name="snapshotPath" /> onto the server's database when it exists and
	///     <paramref name="restoreIfPresent" /> is <see langword="true" />; otherwise captures the
	///     server's current database into <paramref name="snapshotPath" />.
	/// </summary>
	/// <returns><see langword="true" /> if a snapshot was restored; <see langword="false" /> if one was captured.</returns>
	public static bool SyncSnapshot(string serverWorkingDirectory, string snapshotPath, bool restoreIfPresent)
	{
		var dbPath = Path.Combine(serverWorkingDirectory, "Data", "Basil.db");

		if (restoreIfPresent && File.Exists(snapshotPath))
		{
			CopyAll(snapshotPath, dbPath);
			return true;
		}

		CopyAll(dbPath, snapshotPath);
		return false;
	}

	private static void CopyAll(string fromBase, string toBase)
	{
		var directory = Path.GetDirectoryName(toBase);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		foreach (var suffix in Suffixes)
		{
			var source = fromBase + suffix;
			var destination = toBase + suffix;
			if (File.Exists(source)) File.Copy(source, destination, true);
			else if (File.Exists(destination)) File.Delete(destination);
		}
	}
}