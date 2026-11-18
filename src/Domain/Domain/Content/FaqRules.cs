using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

public static class FaqRules
{
	public static bool IsValidName(string? name)
	{
		return name is not null && !name.Split(':').Any(string.IsNullOrWhiteSpace) &&
		       PathValidation.IsValidPath(FileNameOf(name));
	}

	public static string FileNameOf(string name)
	{
		return string.Join('/', name.Split(':')) + ".txt";
	}
}