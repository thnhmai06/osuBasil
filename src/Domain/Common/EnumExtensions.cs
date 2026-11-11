namespace Basil.Domain.Common;

/// <summary>Validation helpers for enum values.</summary>
public static class EnumExtensions
{
	/// <summary>
	///     Throws when a value is not a member of its enum; for a <see cref="FlagsAttribute" /> enum, when
	///     it sets a bit that no member defines.
	/// </summary>
	/// <param name="value">The value to check.</param>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="value" /> is not a valid value of
	///     <typeparamref name="TEnum" />.
	/// </exception>
	/// <returns><paramref name="value" />, unchanged.</returns>
	public static TEnum ThrowIfUndefined<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		return IsValid(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	}

	/// <summary>Tells whether a value is a member of its enum; for a <see cref="FlagsAttribute" /> enum, whether it sets only bits that a member defines.</summary>
	/// <param name="value">The value to check.</param>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <returns><see langword="true" /> when the value is valid for <typeparamref name="TEnum" />.</returns>
	public static bool IsValid<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), false)) return Enum.IsDefined(value);

		var known = Enum.GetValues<TEnum>().Aggregate(0UL, (current, member) => current | Convert.ToUInt64(member));
		return (Convert.ToUInt64(value) & ~known) == 0;
	}
}