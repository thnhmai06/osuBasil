namespace Basil.Host.Cli.Dashboard.Shell;

internal enum Ink : byte
{
	Text,

	Muted,

	Accent,

	Success,

	Warning,

	Error,

	Info,

	Bright,

	RoleStaff,

	RoleModerator,

	RoleSupporter,

	RolePlayer,

	RoleRestricted,

	MapRanked,

	MapLoved,

	MapQualified,

	MapPending,

	GradeTop,

	GradeA,

	GradeB,

	GradeC,

	GradeD,

	RoomOpen,

	RoomPlaying,

	RoomLocked,

	RoomClosed,

	ChatBot
}

internal static class InkTable
{
	public static Ink Basic(Ink ink)
	{
		return ink switch
		{
			Ink.RoleStaff or Ink.MapLoved or Ink.GradeTop or Ink.ChatBot => Ink.Accent,
			Ink.RoleModerator or Ink.GradeD => Ink.Error,
			Ink.RoleSupporter => Ink.Warning,
			Ink.RolePlayer => Ink.Bright,
			Ink.RoleRestricted => Ink.Muted,
			Ink.MapRanked or Ink.GradeA or Ink.RoomPlaying => Ink.Success,
			Ink.MapQualified or Ink.GradeB or Ink.RoomOpen => Ink.Info,
			Ink.GradeC or Ink.RoomLocked => Ink.Warning,
			Ink.MapPending or Ink.RoomClosed => Ink.Muted,
			_ => ink
		};
	}
}

internal sealed record Palette(bool NoColor)
{
	public string Wrap(Ink token, string text)
	{
		var escaped = Escape(text);
		var tag = Tag(token);
		return tag.Length == 0 ? escaped : $"[{tag}]{escaped}[/]";
	}

	public static string Escape(string text)
	{
		return XenoAtom.Ansi.AnsiMarkup.Escape(Plain(text));
	}

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
			Ink.Bright => "brightyellow",
			_ => string.Empty
		};
	}
}
