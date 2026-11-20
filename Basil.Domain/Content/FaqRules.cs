using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

/// <summary>The naming rules of FAQ entries.</summary>
public static class FaqRules
{
	/// <summary>Checks whether a text names a FAQ entry: parts separated by colons, none of them blank, that make a valid file path.</summary>
	/// <param name="name">The entry name.</param>
	/// <returns><see langword="true" /> when the name is valid.</returns>
	public static bool IsValidName(string? name)
	{
		return name is not null && !name.Split(':').Any(string.IsNullOrWhiteSpace) &&
		       PathValidation.IsValidPath(FileNameOf(name));
	}

	/// <summary>Gets the relative file name of a FAQ entry: its parts as folders and a <c>.txt</c> extension.</summary>
	/// <param name="name">The entry name.</param>
	/// <returns>The relative file name; the name is not checked.</returns>
	public static string FileNameOf(string name)
	{
		return string.Join('/', name.Split(':')) + ".txt";
	}
}