using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell.Toasts;

internal sealed class ToastTray
{
	public const int Width = 40;

	public const int Height = 3;

	public const int MaxVisible = 4;

	public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

	public static readonly TimeSpan ErrorLifetime = TimeSpan.FromSeconds(10);

	private readonly TimeProvider _clock;
	private readonly List<Entry> _entries = [];
	private readonly Glyphs _glyphs;
	private readonly Palette _palette;
	private readonly Action<Action> _post;
	private readonly State<int> _version = new(0);

	public ToastTray(Glyphs glyphs, Palette palette, TimeProvider clock, Action<Action> post)
	{
		_glyphs = glyphs;
		_palette = palette;
		_clock = clock;
		_post = post;
	}

	public bool Any => _entries.Count > 0;

	public Visual Over(Visual page)
	{
		var slots = new VStack().Spacing(0).HorizontalAlignment(Align.End).VerticalAlignment(Align.End)
			.Margin(new Thickness(0, 0, 1, 0));
		for (var i = 0; i < MaxVisible; i++)
		{
			var slot = i;
			var box = new Group().Style(_glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Single)
				.IsVisible(() => Visible(slot)).MinWidth(Width).MaxWidth(Width).MinHeight(Height).MaxHeight(Height)
				.Content(new Markup(() => Line(slot)));
			box.PointerPressed(() => Dismiss(slot));
			slots.Add(box);
		}

		return new ZStack(page, slots);
	}

	public void Show(string text, ToastKind kind = ToastKind.Info)
	{
		_post(() =>
		{
			var entry = _entries.Find(e => e.Text == text);
			if (entry is null)
			{
				if (_entries.Count == MaxVisible) _entries.RemoveAt(0);

				_entries.Add(entry = new Entry(text));
			}
			else
			{
				entry.Count++;
			}

			entry.Kind = (ToastKind)Math.Max((int)entry.Kind, (int)kind);
			entry.Deadline = _clock.GetTimestamp() + (long)((entry.Kind == ToastKind.Error ? ErrorLifetime : Lifetime).TotalSeconds * _clock.TimestampFrequency);
			_version.Value++;
		});
	}

	public void Tick()
	{
		var now = _clock.GetTimestamp();
		if (_entries.RemoveAll(entry => now >= entry.Deadline) > 0) _version.Value++;
	}

	public void DismissAll()
	{
		_entries.Clear();
		_version.Value++;
	}

	private void Dismiss(int slot)
	{
		if (slot >= _entries.Count) return;

		_entries.RemoveAt(slot);
		_version.Value++;
	}

	private bool Visible(int slot)
	{
		_ = _version.Value;
		return slot < _entries.Count;
	}

	// The mark and the message, with the number of repeats at the right edge.
	private string Line(int slot)
	{
		_ = _version.Value;
		if (slot >= _entries.Count) return string.Empty;

		var entry = _entries[slot];
		var (mark, ink) = entry.Kind switch
		{
			ToastKind.Error => (_glyphs.ToastError, Ink.Error),
			ToastKind.Warning => (_glyphs.ToastWarning, Ink.Warning),
			_ => (_glyphs.ToastInfo, Ink.Success)
		};
		var repeats = entry.Count > 1 ? $"×{entry.Count}" : string.Empty;
		var room = Width - 2 - 2 - (repeats.Length > 0 ? repeats.Length + 1 : 0);
		var text = Palette.Plain(entry.Text).Fit(room);
		var gap = new string(' ', Math.Max(Width - 2 - 2 - text.Length - repeats.Length, 0));
		return $"{_palette.Wrap(ink, mark)} {Palette.Escape(text)}{gap}{_palette.Wrap(Ink.Muted, repeats)}";
	}

	private sealed class Entry(string text)
	{
		public string Text { get; } = text;
		public int Count { get; set; } = 1;
		public ToastKind Kind { get; set; }
		public long Deadline { get; set; }
	}
}
