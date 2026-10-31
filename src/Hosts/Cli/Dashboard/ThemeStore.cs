using Basil.Host.Cli.Commands;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard;

/// <summary>The colour theme of the dashboard: a <see cref="ColorScheme" /> chosen by name and saved for the next start.</summary>
public sealed class ThemeStore : IThemeControl
{
	/// <summary>The scheme used until the operator chooses another.</summary>
	public static readonly string DefaultScheme = ColorScheme.RootLoopsDark.Name ?? "RootLoopsDark";

	private readonly string _file;
	private readonly Dictionary<string, ColorScheme> _schemes;

	/// <summary>Creates the store and reads the saved choice.</summary>
	/// <param name="directory">The folder the choice is saved in (<c>Configurations/</c>).</param>
	public ThemeStore(string directory)
	{
		_file = Path.Combine(directory, "dashboard-theme.txt");
		_schemes = ColorScheme.GetPredefinedSchemes().Append(ColorScheme.RootLoopsDark)
			.Where(scheme => scheme.Name is not null)
			.GroupBy(scheme => scheme.Name!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key,
				group => group.First(), StringComparer.OrdinalIgnoreCase);
		var saved = TryRead();
		Current = saved is not null && _schemes.ContainsKey(saved) ? _schemes[saved].Name! : DefaultScheme;
		Theme = Theme.FromScheme(_schemes.GetValueOrDefault(Current) ?? ColorScheme.RootLoopsDark);
	}

	/// <summary>Gets the theme in use.</summary>
	public Theme Theme { get; private set; }

	/// <inheritdoc />
	public IReadOnlyList<string> Names => [.. _schemes.Keys.Order()];

	/// <inheritdoc />
	public string Current { get; private set; }

	/// <inheritdoc />
	public bool Set(string name)
	{
		if (!_schemes.TryGetValue(name, out var scheme)) return false;

		Current = scheme.Name!;
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

	/// <summary>Occurs after the theme changed.</summary>
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