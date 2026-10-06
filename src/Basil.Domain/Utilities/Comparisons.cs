using System.Numerics;

namespace Basil.Domain.Utilities;

public static class Comparisons
{
	public static bool NearlyEqual<T>(this T a, T b, T? tolerance = default) where T : INumber<T>
	{
		if (tolerance is null || tolerance == T.Zero)
			tolerance = T.CreateSaturating(1e-6);
		return T.Abs(a - b) <= tolerance;
	}
}