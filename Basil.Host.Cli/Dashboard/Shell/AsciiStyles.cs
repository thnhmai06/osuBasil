using System.Text;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Replaces the glyphs of the controls that draw their own with ASCII ones, for <c>--ascii</c>.</summary>
internal static class AsciiStyles
{
	public static void Apply(Visual root)
	{
		root.SetStyle(GroupStyle.Key, GroupStyle.Ascii);
		root.SetStyle(DialogStyle.Key, DialogStyle.Ascii);
		root.SetStyle(CheckBoxStyle.Key, CheckBoxStyle.Default with { CheckedGlyph = new Rune('x'), UncheckedGlyph = new Rune(' ') });
		root.SetStyle(TextBoxStyle.Key, TextBoxStyle.Default with { PasswordMaskGlyph = new Rune('*') });
		// The scroll bars are drawn with block characters; the position shows in the keys and the footer instead.
		root.SetStyle(ScrollBarStyle.Key, ScrollBarStyle.Default with { Thickness = 0 });
		root.SetStyle(ScrollViewerStyle.Key, ScrollViewerStyle.Default with { ScrollBarThickness = 0 });
	}
}
