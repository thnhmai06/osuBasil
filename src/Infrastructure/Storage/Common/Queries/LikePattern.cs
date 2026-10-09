namespace Basil.Infrastructure.Storage.Common.Queries;

/// <summary>Builds LIKE patterns that match a user's text literally.</summary>
internal static class LikePattern
{
	/// <summary>Gets a pattern that matches any value containing the text, with no character of it acting as a wildcard.</summary>
	/// <param name="text">The text to look for.</param>
	/// <returns>The pattern, for the default LIKE escape character.</returns>
	public static string Containing(string text)
	{
		return "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
	}
}