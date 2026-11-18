namespace Basil.Domain.Chat;

public static class RoomChannelName
{
	public const string Prefix = "#mp_";

	public static string NameFor(int roomId)
	{
		return $"{Prefix}{roomId}";
	}

	public static bool TryParse(string name, out int roomId)
	{
		roomId = 0;
		if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return false;

		var digits = name.AsSpan(Prefix.Length);
		return !digits.IsEmpty && digits.IndexOfAnyExceptInRange('0', '9') < 0 && int.TryParse(digits, out roomId);
	}
}
