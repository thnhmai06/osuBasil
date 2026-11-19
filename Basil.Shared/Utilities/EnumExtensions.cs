namespace Basil.Shared.Utilities;

public static class EnumExtensions
{
	extension<TEnum>(TEnum value) where TEnum : struct, Enum
	{
		public TEnum ThrowIfInvalid()
		{
			return value.IsValid()
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value, "Enum value is undefined.");
		}

		public bool IsValid()
		{
			if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), false)) return Enum.IsDefined(value);

			var known = Enum.GetValues<TEnum>().Aggregate(0UL, (current, member) => current | Convert.ToUInt64(member));
			return (Convert.ToUInt64(value) & ~known) == 0;
		}
	}
}