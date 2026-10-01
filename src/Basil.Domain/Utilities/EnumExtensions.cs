namespace Basil.Domain.Utilities;

/// <summary>Validation helpers for enum values.</summary>
public static class EnumExtensions
{
	/// <summary>
	///     Throws when a value is not a member of its enum; for a <see cref="FlagsAttribute" /> enum, when
	///     it sets a bit that no member defines.
	/// </summary>
	/// <param name="value">The value to check.</param>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is not a valid value of <typeparamref name="TEnum" />.</exception>
	public static void ThrowIfUndefined<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		if (!IsValid(value))
			throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	}

	private static bool IsValid<TEnum>(TEnum value) where TEnum : struct, Enum
	{
		if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), false)) return Enum.IsDefined(value);

		var known = 0UL;
		foreach (var member in Enum.GetValues<TEnum>()) known |= Convert.ToUInt64(member);
		return (Convert.ToUInt64(value) & ~known) == 0;
	}
}
