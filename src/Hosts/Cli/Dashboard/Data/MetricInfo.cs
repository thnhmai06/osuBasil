namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>The name, unit and explanation of one measurement of the server.</summary>
/// <param name="Label">The name inside its category or group; <c>{0}</c> stands for the part of the key that is data, such as a status class.</param>
/// <param name="Unit">What the value counts.</param>
/// <param name="Description">What it measures and what a high or a low value means.</param>
/// <param name="List">Whether the value is a comma-separated list of numbers.</param>
/// <param name="Alarm">Whether any value above zero is a problem.</param>
internal sealed record MetricInfo(string Label, MetricUnit Unit, string Description, bool List = false, bool Alarm = false);
