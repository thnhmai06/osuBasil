using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Bancho.Models.Auth;

/// <summary>
///     The client hash the osu! client sends at login and with each score:
///     <c>osuPath:adapters:adaptersHash:uninstall:disk:</c>, ending with a colon.
/// </summary>
/// <param name="OsuPathHash">The hash of the osu! installation path.</param>
/// <param name="Adapters">The network adapters string.</param>
/// <param name="AdaptersHash">The hash of the network adapters string.</param>
/// <param name="UninstallHash">The hash of the uninstall id.</param>
/// <param name="DiskSignatureHash">The hash of the disk signature.</param>
public sealed record ClientHash(
	string OsuPathHash,
	string Adapters,
	string AdaptersHash,
	string UninstallHash,
	string DiskSignatureHash)
{
	/// <summary>Reads a client hash from its wire text.</summary>
	/// <param name="text">The wire text, including the trailing colon.</param>
	/// <returns>The client hash.</returns>
	/// <exception cref="FormatException">
	///     <paramref name="text" /> lacks the trailing colon or does not have exactly five components.
	/// </exception>
	public static ClientHash Parse(string text)
	{
		if (!text.EndsWith(':')) throw new FormatException("Client hash is missing trailing delimiter.");
		var parts = text[..^1].Split(':', 5);

		return parts.Length == 5
			? new ClientHash(parts[0], parts[1], parts[2], parts[3], parts[4])
			: throw new FormatException("Client hash must contain 5 components.");
	}

	/// <summary>Reads a client hash from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="hash">The client hash when the text is well formed; otherwise <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse(string? text, [NotNullWhen(true)] out ClientHash? hash)
	{
		hash = null;
		if (text is null) return false;

		try
		{
			hash = Parse(text);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>Returns the wire text of this client hash, ending with a colon.</summary>
	/// <returns>The wire text.</returns>
	public override string ToString()
	{
		return $"{OsuPathHash}:{Adapters}:{AdaptersHash}:{UninstallHash}:{DiskSignatureHash}:";
	}
}