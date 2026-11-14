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
	Info,

	/// <summary>An account that owns or develops the server.</summary>
	RoleStaff,

	/// <summary>An ordinary player account.</summary>
	RolePlayer,

	/// <summary>An account without the right to play.</summary>
	RoleRestricted,

	/// <summary>A beatmapset that is ranked or approved.</summary>
	MapRanked,

	/// <summary>A beatmapset that is loved.</summary>
	MapLoved,

	/// <summary>A beatmapset that is qualified.</summary>
	MapQualified,

	/// <summary>A beatmapset that is not published.</summary>
	MapPending,

	/// <summary>The top grades (S and SS).</summary>
	GradeTop,

	/// <summary>Grade A.</summary>
	GradeA,

	/// <summary>Grade B.</summary>
	GradeB,

	/// <summary>Grade C.</summary>
	GradeC,

	/// <summary>Grade D and failed plays.</summary>
	GradeD,

	/// <summary>A room that is open.</summary>
	RoomOpen,

	/// <summary>A room whose round is running.</summary>
	RoomPlaying,

	/// <summary>A room that is locked.</summary>
	RoomLocked,

	/// <summary>A room that ended.</summary>
	RoomClosed,

	/// <summary>The bot's name in chat.</summary>
	ChatBot
}

/// <summary>Tells which basic kind of text each named kind is shown as; the one table a theme change would edit.</summary>
internal static class InkTable
{
	/// <summary>Gets the basic kind a named kind is shown as.</summary>
	/// <param name="ink">The kind of text.</param>
	/// <returns>One of <see cref="Ink.Text" />, <see cref="Ink.Muted" />, <see cref="Ink.Accent" />, <see cref="Ink.Success" />, <see cref="Ink.Warning" />, <see cref="Ink.Error" /> or <see cref="Ink.Info" />.</returns>
	public static Ink Basic(Ink ink)
	{
		return ink switch
		{
			Ink.RoleStaff or Ink.MapLoved or Ink.GradeTop or Ink.ChatBot => Ink.Accent,
			Ink.RolePlayer => Ink.Text,
			Ink.RoleRestricted or Ink.GradeD => Ink.Error,
			Ink.MapRanked or Ink.GradeA or Ink.RoomPlaying => Ink.Success,
			Ink.MapQualified or Ink.GradeB or Ink.RoomOpen => Ink.Info,
			Ink.GradeC or Ink.RoomLocked => Ink.Warning,
			Ink.MapPending or Ink.RoomClosed => Ink.Muted,
			_ => ink
		};
	}
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
		var escaped = XenoAtom.Ansi.AnsiMarkup.Escape(Plain(text));
		var tag = Tag(token);
		return tag.Length == 0 ? escaped : $"[{tag}]{escaped}[/]";
	}

	/// <summary>Replaces the control characters of untrusted text, so that it cannot send escape sequences to the terminal.</summary>
	/// <param name="text">The text, such as a user name or a log message.</param>
	/// <returns>The text with every control character except the tab shown as <c>?</c>.</returns>
	public static string Plain(string text)
	{
		return text.Any(c => char.IsControl(c) && c != '\t')
			? string.Concat(text.Select(c => char.IsControl(c) && c != '\t' ? '?' : c))
			: text;
	}

	private string Tag(Ink kind)
	{
		var token = InkTable.Basic(kind);
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
