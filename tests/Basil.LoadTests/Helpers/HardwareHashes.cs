namespace Basil.LoadTests.Helpers;

/// <summary>
///     Builds a syntactically valid, deterministic client-hashes field for the bancho login body:
///     <c>{osuPathMd5}:{adapters}:{adaptersMd5}:{uninstallMd5}:{diskSignatureMd5}:</c>. The login
///     path rejects a body with no adapters at all (and the account is never subject to a hardware-ban
///     match here, since load accounts are never banned), so a deterministic-but-unique value per
///     account index is enough.
/// </summary>
public static class HardwareHashes
{
	/// <summary>Builds the client-hashes field for the given account index.</summary>
	/// <param name="accountIndex">A stable, unique index identifying the virtual user.</param>
	/// <returns>The pipe-field value, including the trailing colon the login parser requires.</returns>
	public static string ForAccount(int accountIndex)
	{
		var osuPathMd5 = Md5Hex.Of($"osu-path-{accountIndex}");
		var adapters = $"loadtest-adapter-{accountIndex}.";
		var adaptersMd5 = Md5Hex.Of(adapters);
		var uninstallMd5 = Md5Hex.Of($"uninstall-{accountIndex}");
		var diskSignatureMd5 = Md5Hex.Of($"disk-{accountIndex}");
		return $"{osuPathMd5}:{adapters}:{adaptersMd5}:{uninstallMd5}:{diskSignatureMd5}:";
	}
}