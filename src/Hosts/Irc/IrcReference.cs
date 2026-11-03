using System.Text;
using Basil.Host.Shared.Documentation;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc;

/// <summary>Writes what an IRC client can do on this server: how to reach it, the capabilities it grants and the commands it answers.</summary>
/// <param name="options">The address the server listens on.</param>
public sealed class IrcReference(IOptions<IrcOptions> options) : IDocumentPage
{
	/// <summary>The commands the server answers, with whether they need the connection to be signed in first.</summary>
	internal static readonly IReadOnlyList<(string Command, bool SignedIn, string Summary)> Commands =
	[
		("CAP", false, "Negotiate the IRCv3 capabilities (`LS`, `LIST`, `REQ`, `END`)."),
		("PASS", false, "Send the IRC key before registering."),
		("NICK", false, "Give the nickname: your username, with spaces written as `_`."),
		("USER", false, "Complete the registration."),
		("AUTHENTICATE", false, "Sign in with SASL `PLAIN`: your username and the IRC key."),
		("PING", false, "Check that the server answers; `PONG` is accepted back."),
		("QUIT", false, "Close the connection."),
		("PRIVMSG", true, "Send a message to a channel or a user, or a command to BasilBot."),
		("NOTICE", true, "Send a notice; handled like `PRIVMSG`."),
		("JOIN", true, "Join a channel you may read."),
		("PART", true, "Leave a channel."),
		("NAMES", true, "List who is in a channel."),
		("LIST", true, "List the channels you may see."),
		("WHO", true, "Query users."),
		("WHOIS", true, "Query one user."),
		("ISON", true, "Ask which nicknames are online."),
		("USERHOST", true, "Ask for the host of nicknames."),
		("AWAY", true, "Set or clear your away message."),
		("TOPIC", true, "Read a channel's topic; changing it is refused."),
		("MODE", true, "Read a channel's or your own modes; changing them is refused."),
		("MOTD", true, "Show the message of the day."),
		("KICK", true, "Always refused: channels are managed through the API or `!channel`."),
		("INVITE", true, "Always refused: channels are managed through the API or `!channel`.")
	];

	/// <inheritdoc />
	public string Slug => "irc";

	/// <inheritdoc />
	public string Title => "IRC";

	/// <inheritdoc />
	public string Markdown()
	{
		var text = new StringBuilder();
		text.AppendLine("# IRC").AppendLine();
		text.AppendLine("Basil has an IRC server that lets standard IRC clients, bots and bouncers use the same chat as the " +
		                "osu! (stable) client: read channels, talk to other players, message BasilBot and run its commands.")
			.AppendLine();
		text.AppendLine("## Connecting").AppendLine();
		text.AppendLine("| Setting | Value |").AppendLine("| --- | --- |");
		text.AppendLine($"| Server | `{options.Value.ServerName}` |");
		text.AppendLine($"| Port | `{options.Value.Port}`, plain text, no TLS |");
		text.AppendLine("| Nickname | your username, with spaces written as `_` |");
		text.AppendLine("| Password / SASL password | an IRC key, not your account password |").AppendLine();
		text.AppendLine("An IRC key belongs to one device or bot and is created with the HTTP API; ending its session revokes it. " +
		                "A wrong key, or a nickname that is not yours, is answered with `464` and the connection is closed.")
			.AppendLine();
		text.AppendLine("## Capabilities").AppendLine();
		text.AppendLine("`CAP LS` offers: " + string.Join(", ", IrcCommands.Capabilities.Select(cap => $"`{cap}`")) + ".")
			.AppendLine();
		text.AppendLine("## Commands").AppendLine();
		text.AppendLine("| Command | Needs sign-in | What it does |").AppendLine("| --- | --- | --- |");
		foreach (var (command, signedIn, summary) in Commands)
			text.AppendLine($"| `{command}` | {(signedIn ? "yes" : "no")} | {summary} |");
		text.AppendLine().AppendLine("Any other command is answered with `421`.").AppendLine();
		text.AppendLine("## In Basil").AppendLine();
		text.AppendLine("* Room chat is the channel `#mp_<room id>`.");
		text.AppendLine("* Message `BasilBot` (`/msg BasilBot !help`) or type `!` commands in a channel; they run with your own rights.");
		text.AppendLine("* The server keeps no chat history.");
		return text.ToString();
	}
}
