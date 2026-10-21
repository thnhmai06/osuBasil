namespace Basil.Bot.Replies;

/// <summary>Where BasilBot finds the wording of its replies.</summary>
public sealed class RepliesOptions
{
	/// <summary>Gets or sets the path of the replies file, which the bot writes with its default wording when it is absent.</summary>
	public string Path { get; set; } = "Data/Bot/replies.json";
}
