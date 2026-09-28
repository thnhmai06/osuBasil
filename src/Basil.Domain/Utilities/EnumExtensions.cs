namespace Basil.Domain.Utilities;

public static class EnumExtensions
{
	public static void ThrowIfUndefined<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		if (!Enum.IsDefined(value))
			throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
	}
}