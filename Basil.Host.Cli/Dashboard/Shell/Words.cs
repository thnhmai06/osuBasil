using System.Text;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class Words
{
	public static string Humanise(string name)
	{
		var text = new StringBuilder(name.Length + 4);
		for (var i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) text.Append(' ');
			text.Append(i == 0 ? char.ToUpperInvariant(name[i]) : char.ToLowerInvariant(name[i]));
		}

		return text.ToString();
	}
}