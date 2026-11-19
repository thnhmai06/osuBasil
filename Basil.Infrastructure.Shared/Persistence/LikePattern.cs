namespace Basil.Infrastructure.Shared.Persistence;

public static class LikePattern
{
	public const string Escape = "!";

	public static string Containing(string text)
	{
		return "%" + Literal(text) + "%";
	}

	public static string Literal(string text)
	{
		return text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
	}
}