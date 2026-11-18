using Basil.Host.Cli.Commands;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard;

public sealed class ThemeStore : IThemeControl
{
	public static readonly string DefaultScheme = ColorScheme.RootLoopsDark.Name;

	private readonly string _file;
	private readonly Dictionary<string, ColorScheme> _schemes;

	public ThemeStore(string directory, bool monochrome = false)
	{
		Locked = monochrome;
		if (monochrome)
		{
			_file = string.Empty;
			_schemes = [];
			Current = MonochromeName;
			Theme = Theme.Terminal;
			return;
		}

		_file = Path.Combine(directory, "dashboard-theme.txt");
		_schemes = ColorScheme.GetPredefinedSchemes().Append(ColorScheme.RootLoopsDark)
			.GroupBy(scheme => scheme.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key,
				group => group.First(), StringComparer.OrdinalIgnoreCase);
		var saved = TryRead();
		Current = saved is not null && _schemes.TryGetValue(saved, out var s) ? s.Name : DefaultScheme;
		Theme = Theme.FromScheme(_schemes.GetValueOrDefault(Current) ?? ColorScheme.RootLoopsDark);
	}

	public const string MonochromeName = "monochrome";

	public bool Locked { get; }

	public Theme Theme { get; private set; }

	public IReadOnlyList<string> Names => [.. _schemes.Keys.Order()];

	public string Current { get; private set; }

	public bool Set(string name)
	{
		if (Locked) return false;

		if (!_schemes.TryGetValue(name, out var scheme)) return false;

		Current = scheme.Name;
		Theme = Theme.FromScheme(scheme);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
			File.WriteAllText(_file, Current);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The theme still changes for this run; it just is not remembered.
		}

		Changed?.Invoke(Theme);
		return true;
	}

	public event Action<Theme>? Changed;

	private string? TryRead()
	{
		try
		{
			return File.Exists(_file) ? File.ReadAllText(_file).Trim() : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}