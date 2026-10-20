using System.Numerics;

namespace Basil.Domain.Common;

/// <summary>Comparisons of numbers.</summary>
public static class Comparisons
{
	/// <summary>Determines whether two numbers differ by no more than a tolerance.</summary>
	/// <param name="a">The first number.</param>
	/// <param name="b">The second number.</param>
	/// <param name="tolerance">The largest accepted difference; when omitted or zero, one millionth.</param>
	/// <typeparam name="T">The number type.</typeparam>
	/// <returns><see langword="true" /> when the absolute difference is within the tolerance.</returns>
	public static bool NearlyEqual<T>(this T a, T b, T? tolerance = default) where T : INumber<T>
	{
		if (tolerance is null || tolerance == T.Zero)
			tolerance = T.CreateSaturating(1e-6);
		return T.Abs(a - b) <= tolerance;
	}
}