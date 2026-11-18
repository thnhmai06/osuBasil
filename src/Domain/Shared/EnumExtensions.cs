namespace Basil.Domain.Shared;

public static class EnumExtensions
{
	public static TEnum ThrowIfUndefined<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		return value.IsValid()
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	}

	public static bool IsValid<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), false)) return Enum.IsDefined(value);

		var known = Enum.GetValues<TEnum>().Aggregate(0UL, (current, member) => current | Convert.ToUInt64(member));
		return (Convert.ToUInt64(value) & ~known) == 0;
	}
}