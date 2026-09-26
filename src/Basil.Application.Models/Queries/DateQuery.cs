namespace Basil.Application.Models.Queries;

/// <summary>
///     A `key&lt;operator&gt;value` comparison against a stored instant, where the query's value may
///     name only a year, a year and month, or a year/month/day -- in which case it names a whole
///     window of time rather than one precise instant.
/// </summary>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="RangeStart">The start of the window the query's value names (inclusive).</param>
/// <param name="RangeEnd">
///     The end of the window the query's value names (exclusive) -- equal to
///     <paramref name="RangeStart" /> when the query gave a precise instant rather than a
///     year/month/day.
/// </param>
/// <remarks>
///     <see cref="ComparisonOperator.Equal" /> matches anywhere inside
///     [<see cref="RangeStart" />, <see cref="RangeEnd" />); <see cref="ComparisonOperator.GreaterThan" />
///     and <see cref="ComparisonOperator.LessThanOrEqual" /> both anchor to
///     <see cref="RangeEnd" /> (strictly after the whole window, or anywhere up through it);
///     <see cref="ComparisonOperator.GreaterThanOrEqual" /> and <see cref="ComparisonOperator.LessThan" />
///     both anchor to <see cref="RangeStart" /> (at or after the window begins, or strictly before it
///     begins).
/// </remarks>
public sealed record DateQuery(ComparisonOperator Operator, DateTimeOffset RangeStart, DateTimeOffset RangeEnd);