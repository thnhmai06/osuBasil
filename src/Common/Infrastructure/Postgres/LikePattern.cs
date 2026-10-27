namespace Basil.Common.Infrastructure.Postgres;

/// <summary>Builds LIKE patterns that match a user's text literally.</summary>
public static class LikePattern
{
	/// <summary>
	///     The escape character of the patterns; a query must name it, or the escape sequences of a pattern are matched
	///     literally.
	/// </summary>
	public const string Escape = "\\";

	/// <summary>Gets a pattern that matches any value containing the text, with no character of it acting as a wildcard.</summary>
	/// <param name="text">The text to look for.</param>
	/// <returns>The pattern, for the default LIKE escape character.</returns>
	public static string Containing(string text)
	{
		return "%" + Literal(text) + "%";
	}

	/// <summary>Gets a pattern that matches exactly the text, with no character of it acting as a wildcard.</summary>
	/// <param name="text">The text to match.</param>
	/// <returns>The pattern, for the default LIKE escape character.</returns>
	public static string Literal(string text)
	{
		return text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
	}
}