using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>One measurement as the monitor lists it inside the box of its category.</summary>
/// <param name="Key">The dotted name in the sample, which identifies the measurement.</param>
/// <param name="Group">The sub-group title inside the category, or an empty string.</param>
/// <param name="Label">The name relative to its category and group.</param>
/// <param name="Value">The value, written for the operator.</param>
/// <param name="Ink">The kind of text the value is.</param>
internal sealed record MetricLine(string Key, string Group, string Label, string Value, Ink Ink);
