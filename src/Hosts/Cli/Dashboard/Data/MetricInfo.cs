namespace Basil.Host.Cli.Dashboard.Data;

internal sealed record MetricInfo(string Label, MetricUnit Unit, string Description, IReadOnlyList<MetricPart>? Parts = null, bool Alarm = false);
