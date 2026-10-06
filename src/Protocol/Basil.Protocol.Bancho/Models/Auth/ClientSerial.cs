using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Bancho.Models.Auth;

/// <summary>The client's unique ids the osu! client sends with a score, as <c>uninstallId|diskSignature</c>.</summary>
/// <param name="UninstallId">The uninstall id.</param>
/// <param name="DiskSignature">The disk signature.</param>
public sealed record ClientSerial(string UninstallId, string DiskSignature)
{
	/// <summary>Reads a client serial from its wire text.</summary>
	/// <param name="text">The wire text.</param>
	/// <returns>The client serial.</returns>
	/// <exception cref="FormatException"><paramref name="text" /> has no <c>|</c> separator.</exception>
	public static ClientSerial Parse(string text)
	{
		var parts = text.Split('|', 2);

		return parts.Length == 2
			? new ClientSerial(parts[0], parts[1])
			: throw new FormatException("Client serial must contain 2 components.");
	}

	/// <summary>Reads a client serial from its wire text without throwing.</summary>
	/// <param name="text">The wire text, or <see langword="null" />.</param>
	/// <param name="serial">The client serial when the text is well formed; otherwise <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="text" /> was read; otherwise, <see langword="false" />.</returns>
	public static bool TryParse(string? text, [NotNullWhen(true)] out ClientSerial? serial)
	{
		serial = null;
		if (text is null) return false;

		try
		{
			serial = Parse(text);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	/// <summary>Returns the wire text of this client serial.</summary>
	/// <returns>The wire text.</returns>
	public override string ToString()
	{
		return $"{UninstallId}|{DiskSignature}";
	}
}
