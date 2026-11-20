namespace Basil.Domain.Chat;

/// <summary>The naming rules of the chat channel that belongs to a multiplayer room.</summary>
public static class RoomChannelRules
{
	/// <summary>The text every room channel name starts with.</summary>
	public const string Prefix = "#mp_";

	/// <summary>Gets the name of the channel of a room.</summary>
	/// <param name="roomId">The identifier of the room.</param>
	/// <returns>The channel name.</returns>
	public static string NameFor(int roomId)
	{
		return $"{Prefix}{roomId}";
	}

	/// <summary>Reads the room identifier out of a room channel name.</summary>
	/// <param name="name">The channel name.</param>
	/// <param name="roomId">The identifier of the room, or 0 when the name is not a room channel name.</param>
	/// <returns><see langword="true" /> when the name is the prefix followed by digits only that fit a room identifier.</returns>
	public static bool TryParse(string name, out int roomId)
	{
		roomId = 0;
		if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return false;

		var digits = name.AsSpan(Prefix.Length);
		return !digits.IsEmpty && digits.IndexOfAnyExceptInRange('0', '9') < 0 && int.TryParse(digits, out roomId);
	}
}