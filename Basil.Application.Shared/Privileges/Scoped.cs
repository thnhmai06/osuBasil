using System.Reflection;

namespace Basil.Application.Shared.Privileges;

public static class ScopedPrivileges
{
	public static bool Lacks<TPrivileges>(TPrivileges held, TPrivileges required)
		where TPrivileges : struct, Enum
	{
		var have = Convert.ToInt64(held);
		var needed = Convert.ToInt64(required);

		return Flags<TPrivileges>.IsFlags ? (needed & ~have) != 0 : have < needed;
	}

	private static class Flags<TPrivileges> where TPrivileges : struct, Enum
	{
		public static readonly bool IsFlags = typeof(TPrivileges).GetCustomAttribute<FlagsAttribute>() is not null;
	}
}