namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The characters the dashboard draws with, plain ASCII when <c>--ascii</c> is given.</summary>
/// <param name="Ascii">Whether the characters stay in ASCII.</param>
internal sealed record Glyphs(bool Ascii)
{
	/// <summary>The mark in front of what the operator typed.</summary>
	public string Prompt => Ascii ? ">" : "❯";

	/// <summary>The mark of a failed dimension on the too-small screen.</summary>
	public string Cross => Ascii ? "x" : "✗";

	/// <summary>The separator of the hints of the status line.</summary>
	public string Dot => Ascii ? "|" : "·";

	/// <summary>The mark of a missing or yes/no value.</summary>
	public string Yes => Ascii ? "y" : "✓";

	/// <summary>The mark of a no value.</summary>
	public string No => Ascii ? "n" : "×";
}
