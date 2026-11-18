using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Dashboard.Data;

internal sealed record MetricLine(string Key, string Group, string Label, string Value, Ink Ink);
