namespace Basil.Application.Queries;

/// <summary>The comparison an individual filter applies.</summary>
public enum ComparisonOperator : byte
{
	/// <summary>The stored value must equal the filter's value.</summary>
	Equal,

	/// <summary>The stored value must be less than the filter's value.</summary>
	LessThan,

	/// <summary>The stored value must be less than or equal to the filter's value.</summary>
	LessThanOrEqual,

	/// <summary>The stored value must be greater than the filter's value.</summary>
	GreaterThan,

	/// <summary>The stored value must be greater than or equal to the filter's value.</summary>
	GreaterThanOrEqual
}

/// <summary>A single `key&lt;operator&gt;value` search-query comparison against one stored field.</summary>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="Value">The value to compare the stored field against.</param>
public sealed record ComparableFilter<T>(ComparisonOperator Operator, T Value);

/// <summary>
///     Provides extension methods for working with <see cref="ComparisonOperator" /> values.
/// </summary>
public static class ComparisonOperatorExtensions
{
	/// <summary>
	///     Parses a comparison operator token into a <see cref="ComparisonOperator" />.
	/// </summary>
	/// <param name="op">
	///     The operator token: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>==</c>,
	///     <c>=</c>, or <c>:</c>.
	/// </param>
	/// <returns>The corresponding <see cref="ComparisonOperator" /> value.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="op" /> is not one of the recognized operator tokens.
	/// </exception>
	public static ComparisonOperator Parse(string op)
	{
		return op switch
		{
			"<" => ComparisonOperator.LessThan,
			"<=" => ComparisonOperator.LessThanOrEqual,
			">" => ComparisonOperator.GreaterThan,
			">=" => ComparisonOperator.GreaterThanOrEqual,
			"==" or "=" or ":" => ComparisonOperator.Equal,
			_ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
		};
	}
}