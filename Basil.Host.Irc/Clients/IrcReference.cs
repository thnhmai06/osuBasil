using System.Text;
using Basil.Host.Irc.Commands;
using Basil.Host.Irc.Transport;
using Basil.Host.Shared.Documentation;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Clients;

public sealed class IrcReference(IOptions<IrcOptions> options) : IDocumentPage
{
	internal static readonly IReadOnlyList<(string Command, bool SignedIn, string Summary)> Commands =
	[
		("CAP", false, "Negotiate the IRCv3 capabilities (`LS`, `LIST`, `REQ`, `END`)."),
		("PASS", false, "Send your account password before registering."),
		("NICK", false, "Give the nickname: your username, with spaces written as `_`."),
		("USER", false, "Complete the registration."),
		("AUTHENTICATE", false, "Sign in with SASL `PLAIN`: your username and your account password."),
		("PING", false, "Check that the server answers; `PONG` is accepted back."),
		("QUIT", false, "Close the connection."),
		("PRIVMSG", true, "Send a message to a channel or a user, or a command to BasilBot."),
		("NOTICE", true, "Send a notice; handled like `PRIVMSG`."),
		("JOIN", true, "Join a channel you may read; spectator channels (`#spec_`) are not offered over IRC."),
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

	public string Slug => "irc";

	public string Title => "IRC";

	public string Markdown()
	{
		var text = new StringBuilder();
		text.AppendLine("# IRC").AppendLine();
		text.AppendLine(
				"Basil has an IRC server that lets standard IRC clients, bots and bouncers use the same chat as the " +
				"osu! (stable) client: read channels, talk to other players, message BasilBot and run its commands.")
			.AppendLine();
		text.AppendLine("## Connecting").AppendLine();
		text.AppendLine("| Setting | Value |").AppendLine("| --- | --- |");
		text.AppendLine($"| Server | `{options.Value.ServerName}` |");
		// The port clients reach is nginx's TLS port, not the internal one the listener binds.
		text.AppendLine("| Port | `6697`, TLS |");
		text.AppendLine("| Nickname | your username, with spaces written as `_` |");
		text.AppendLine("| Password / SASL password | your account password |").AppendLine();
		text.AppendLine(
				"The connection is encrypted with TLS; a server with a self-signed certificate needs your client to trust " +
				"it. An account holds one IRC sign-in at a time. A wrong password, or an account already signed in on " +
				"IRC, is answered with `464` and the connection is closed.")
			.AppendLine();
		text.AppendLine("## Capabilities").AppendLine();
		text.AppendLine("`CAP LS` offers: " + string.Join(", ", IrcCommands.Capabilities.Select(cap => $"`{cap}`")) +
		                ".")
			.AppendLine();
		text.AppendLine("## Commands").AppendLine();
		text.AppendLine("| Command | Needs sign-in | What it does |").AppendLine("| --- | --- | --- |");
		foreach (var (command, signedIn, summary) in Commands)
			text.AppendLine($"| `{command}` | {(signedIn ? "yes" : "no")} | {summary} |");
		text.AppendLine().AppendLine("Any other command is answered with `421`.").AppendLine();
		text.AppendLine("## In Basil").AppendLine();
		text.AppendLine("* Room chat is the channel `#mp_<room id>`.");
		text.AppendLine(
			"* Message `BasilBot` (`/msg BasilBot !help`) or type `!` commands in a channel; they run with your own privileges.");
		text.AppendLine("* The server keeps no chat history.");
		return text.ToString();
	}
}