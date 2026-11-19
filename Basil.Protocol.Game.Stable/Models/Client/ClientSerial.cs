using System.Diagnostics.CodeAnalysis;

namespace Basil.Protocol.Game.Stable.Models.Client;

public sealed record ClientSerial(string UninstallId, string DiskSignature)
{
	public static ClientSerial Parse(string text)
	{
		return TryParse(text, out var serial) ? serial : throw new FormatException("The client serial is malformed.");
	}

	public static bool TryParse(string? text, [NotNullWhen(true)] out ClientSerial? serial)
	{
		serial = null;
		if (text is null) return false;

		var parts = text.Split('|', 2);
		if (parts.Length != 2) return false;

		serial = new ClientSerial(parts[0], parts[1]);
		return true;
	}

	public override string ToString()
	{
		return $"{UninstallId}|{DiskSignature}";
	}
}