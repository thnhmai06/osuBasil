namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>One value a form asks for.</summary>
/// <param name="Label">The name shown beside the box.</param>
/// <param name="Start">The text the box starts with.</param>
/// <param name="Secret">Whether the box hides what is typed.</param>
internal sealed record FormField(string Label, string Start = "", bool Secret = false);
