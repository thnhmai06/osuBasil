namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The characters the dashboard draws with, plain ASCII when <c>--ascii</c> is given.</summary>
/// <param name="Ascii">Whether the characters stay in ASCII.</param>
internal sealed record Glyphs(bool Ascii)
{
	/// <summary>The mark in front of what the operator typed.</summary>
	public string Prompt => Ascii ? ">" : "❯";

	/// <summary>The separator of the hints of the status line.</summary>
	public string Dot => Ascii ? "|" : "·";

	/// <summary>The mark of a missing or yes/no value.</summary>
	public string Yes => Ascii ? "y" : "✓";

	/// <summary>The mark of a no value.</summary>
	public string No => Ascii ? "n" : "×";

	/// <summary>The mark of a toast that tells how something went well.</summary>
	public string ToastInfo => Ascii ? "+" : "✓";

	/// <summary>The mark of a toast that warns.</summary>
	public string ToastWarning => "!";

	/// <summary>The mark of a toast that tells something failed.</summary>
	public string ToastError => Ascii ? "x" : "✗";

	/// <summary>The mark of a title whose group is open.</summary>
	public string Open => Ascii ? "v" : "▾";

	/// <summary>The mark of a title whose group is folded.</summary>
	public string Folded => Ascii ? ">" : "▸";

	/// <summary>The line of a group title.</summary>
	public char Rule => Ascii ? '-' : '─';

	/// <summary>The mark between the parts of a breadcrumb.</summary>
	public string Crumb => Ascii ? ">" : "›";

	/// <summary>The mark of the Refresh button.</summary>
	public string Refresh => Ascii ? "@" : "↻";

	/// <summary>The mark of the Back button.</summary>
	public string Back => Ascii ? "<" : "←";

	/// <summary>The mark of the Jump to End button.</summary>
	public string Down => Ascii ? "v" : "↓";
}
