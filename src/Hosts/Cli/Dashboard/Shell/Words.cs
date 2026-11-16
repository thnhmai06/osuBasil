using System.Text;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Turns the names of settings into text an operator reads.</summary>
internal static class Words
{
	/// <summary>Writes an identifier as words: <c>rateLimits</c> becomes <c>Rate limits</c>, only the first letter in capital.</summary>
	/// <param name="name">The identifier, in camel or Pascal case.</param>
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
