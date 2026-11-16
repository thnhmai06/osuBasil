namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>The name, unit and explanation of one measurement of the server.</summary>
/// <param name="Label">The name inside its category or group; <c>{0}</c> stands for the part of the key that is data, such as a status class.</param>
/// <param name="Unit">What the value counts.</param>
/// <param name="Description">What it measures and what a high or a low value means.</param>
/// <param name="Parts">The names and explanations of the numbers of a value that is a comma-separated list, one measurement each; <see langword="null" /> for a single number.</param>
/// <param name="Alarm">Whether any value above zero is a problem.</param>
internal sealed record MetricInfo(string Label, MetricUnit Unit, string Description, IReadOnlyList<MetricPart>? Parts = null, bool Alarm = false);
