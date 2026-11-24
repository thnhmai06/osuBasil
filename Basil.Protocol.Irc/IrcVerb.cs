namespace Basil.Protocol.Irc;

/// <summary>The command words of IRC.</summary>
public static class IrcVerb
{
	public const string Cap = "CAP";
	public const string Pass = "PASS";
	public const string Nick = "NICK";
	public const string User = "USER";
	public const string Authenticate = "AUTHENTICATE";
	public const string Ping = "PING";
	public const string Pong = "PONG";
	public const string Quit = "QUIT";
	public const string Error = "ERROR";
	public const string Privmsg = "PRIVMSG";
	public const string Notice = "NOTICE";
	public const string Join = "JOIN";
	public const string Part = "PART";
	public const string Names = "NAMES";
	public const string List = "LIST";
	public const string Who = "WHO";
	public const string Whois = "WHOIS";
	public const string Ison = "ISON";
	public const string Userhost = "USERHOST";
	public const string Away = "AWAY";
	public const string Topic = "TOPIC";
	public const string Mode = "MODE";
	public const string Motd = "MOTD";
	public const string Kick = "KICK";
	public const string Invite = "INVITE";

	/// <summary>The IRCv3 standard reply that reports a failure.</summary>
	public const string Fail = "FAIL";
}
