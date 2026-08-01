using System.Collections.Immutable;

namespace Basil.Domain.Login;

/// <summary>
///     Represents the client details captured from an osu! client at login.
/// </summary>
/// <param name="OsuPathMd5">The MD5 hash of the osu! executable path.</param>
/// <param name="AdaptersMd5">The MD5 hash of the combined adapter names.</param>
/// <param name="UninstallMd5">The MD5 hash of the uninstallation identifier.</param>
/// <param name="DiskSignatureMd5">The MD5 hash of the disk signature.</param>
/// <param name="Adapters">The list of network adapter names.</param>
/// <remarks>
///     Captured once at login and re-checked against every score submission from that session, as
///     part of the submission integrity validation.
/// </remarks>
public sealed record ClientDetails(
	string OsuPathMd5,
	string AdaptersMd5,
	string UninstallMd5,
	string DiskSignatureMd5,
	ImmutableList<string> Adapters)
{
	private const string WineAdapterSentinel = "runningunderwine";

	/// <summary>
	///     Gets a value that indicates whether the client is running under Wine.
	/// </summary>
	/// <value>
	///     <see langword="true" /> when the adapter list contains exactly the Wine sentinel adapter;
	///     otherwise, <see langword="false" />.
	/// </value>
	public bool IsRunningUnderWine => Adapters.Contains(WineAdapterSentinel) && Adapters.Count == 1;

	/// <summary>
	///     Computes the client hash used to verify the client on later requests.
	/// </summary>
	/// <returns>The colon-delimited client hash string.</returns>
	public string Hash()
	{
		var adaptersString = string.Join('.', Adapters);
		if (adaptersString != WineAdapterSentinel) adaptersString += ".";

		return $"{OsuPathMd5}:{adaptersString}:{AdaptersMd5}:{UninstallMd5}:{DiskSignatureMd5}:";
	}

	/// <summary>
	///     Parses a client hash string into <see cref="ClientDetails" />.
	/// </summary>
	/// <param name="hash">The colon-delimited client hash string sent by the client.</param>
	/// <returns>The parsed client details.</returns>
	/// <exception cref="FormatException">The adapter list is missing its trailing delimiter.</exception>
	public static ClientDetails From(string hash)
	{
		var hashParts = hash[..^1].Split(':', 5);

		var osuPathMd5 = hashParts[0];
		var adaptersString = hashParts[1];
		var adaptersMd5 = hashParts[2];
		var uninstallMd5 = hashParts[3];
		var diskSignatureMd5 = hashParts[4];

		var adapters = ParseAdapters(adaptersString);

		return new ClientDetails(osuPathMd5, adaptersMd5, uninstallMd5, diskSignatureMd5, adapters);
	}

	private static ImmutableList<string> ParseAdapters(string adaptersString)
	{
		if (adaptersString == WineAdapterSentinel) return [WineAdapterSentinel];
		return adaptersString.EndsWith('.')
			? [.. adaptersString[..^1].Split('.')]
			: throw new FormatException("Adapter list is missing trailing delimiter");
	}
}