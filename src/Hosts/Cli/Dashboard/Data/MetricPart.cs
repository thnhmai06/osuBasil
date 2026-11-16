namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>One number of a measurement that the sample reports as a list, such as the size of one generation of the heap.</summary>
/// <param name="Label">The name of the number inside its category.</param>
/// <param name="Description">What it measures and what a high or a low value means.</param>
internal sealed record MetricPart(string Label, string Description);
