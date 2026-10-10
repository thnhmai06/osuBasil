namespace Basil.Bot.Application;

/// <summary>Configuration options for BasilBot.</summary>
public sealed class BotOptions
{
	/// <summary>The text that starts a command.</summary>
	public string Prefix { get; set; } = "!";
}
