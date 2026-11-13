namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The kinds of text the dashboard colours; the theme decides what colour each one has.</summary>
internal enum Ink : byte
{
	/// <summary>Ordinary text.</summary>
	Text,

	/// <summary>Less important text.</summary>
	Muted,

	/// <summary>What the operator typed and what stands out.</summary>
	Accent,

	/// <summary>Something that worked.</summary>
	Success,

	/// <summary>Something that needs attention.</summary>
	Warning,

	/// <summary>Something that failed.</summary>
	Error,

	/// <summary>Information.</summary>
	Info
}

/// <summary>Turns a <see cref="Ink" /> into markup; the one place that knows which colour a kind of text has.</summary>
/// <param name="NoColor">Whether the dashboard is monochrome, so that kinds differ by weight only.</param>
internal sealed record Palette(bool NoColor)
{
	/// <summary>Wraps escaped text in the markup of a token.</summary>
	/// <param name="token">The kind of text.</param>
	/// <param name="text">The plain text; it is escaped here.</param>
	public string Wrap(Ink token, string text)
	{
		var escaped = XenoAtom.Ansi.AnsiMarkup.Escape(text);
		var tag = Tag(token);
		return tag.Length == 0 ? escaped : $"[{tag}]{escaped}[/]";
	}

	private string Tag(Ink token)
	{
		if (NoColor)
			return token switch
			{
				Ink.Muted => "dim",
				Ink.Accent or Ink.Error => "bold",
				_ => string.Empty
			};

		return token switch
		{
			Ink.Muted => "gray",
			Ink.Accent => "cyan",
			Ink.Success => "green",
			Ink.Warning => "yellow",
			Ink.Error => "red",
			Ink.Info => "blue",
			_ => string.Empty
		};
	}
}
