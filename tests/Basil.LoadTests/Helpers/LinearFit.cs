namespace Basil.LoadTests.Helpers;

/// <summary>The result of fitting a line to a series of (x, y) samples.</summary>
/// <param name="SlopePerHour">The fitted slope, expressed per hour of <c>x</c> (x is assumed to be seconds).</param>
/// <param name="RSquared">The coefficient of determination, in [0, 1]; higher means a cleaner trend.</param>
public sealed record LinearFitResult(double SlopePerHour, double RSquared);

/// <summary>
///     Ordinary least-squares line fitting, used by the soak analyzer to turn a resource timeline into a
///     slope-per-hour and a confidence figure without pulling in a statistics package for one formula.
/// </summary>
public static class LinearFit
{
	/// <summary>Fits a line through <paramref name="points" />, where <c>x</c> is elapsed seconds.</summary>
	/// <param name="points">The (elapsedSeconds, value) samples to fit, in any order.</param>
	/// <returns><see langword="null" /> when fewer than 2 points are given or <c>x</c> has no spread.</returns>
	public static LinearFitResult? Fit(IReadOnlyList<(double X, double Y)> points)
	{
		if (points.Count < 2) return null;

		var n = points.Count;
		var sumX = points.Sum(p => p.X);
		var sumY = points.Sum(p => p.Y);
		var meanX = sumX / n;
		var meanY = sumY / n;

		var sxx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
		if (sxx == 0) return null;

		var sxy = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
		var slope = sxy / sxx;
		var intercept = meanY - slope * meanX;

		var ssTotal = points.Sum(p => (p.Y - meanY) * (p.Y - meanY));
		var ssResidual = points.Sum(p =>
		{
			var predicted = slope * p.X + intercept;
			return (p.Y - predicted) * (p.Y - predicted);
		});

		var rSquared = ssTotal == 0 ? 1.0 : 1.0 - ssResidual / ssTotal;
		return new LinearFitResult(slope * 3600.0, rSquared);
	}
}