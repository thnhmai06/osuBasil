using System.Text;
using Basil.Host.Shared.Documentation;
using Basil.Protocol.OsuStable.Wire.Packets;

namespace Basil.Host.OsuStable;

/// <summary>Describes the endpoints of the osu! (stable) client for the generated API documentation.</summary>
public static class OsuStableDocumentation
{
	/// <summary>The name of the documentation the endpoints of the osu! (stable) client are listed in.</summary>
	public const string Group = ApiDocument.OsuStable;

	private const string Overview = """
	                                The endpoints the osu! (stable) game client calls. They are not an API for other programs; the client
	                                fixes their addresses, parameters and answers, which are plain text unless stated.

	                                **Hosts.** The game server (`POST /`) answers on `c.`, `ce.`, `c4.`, `c5.` and `c6.<domain>`; the web
	                                endpoints (`/web/...`, `/users`, `/d/...`) answer on `osu.<domain>`. Despite the address this page is
	                                served from, call them on those hosts.

	                                **Authentication.** Web endpoints take the player's name and password hash (MD5) as query or form fields,
	                                as named for each endpoint. The game server uses the `osu-token` header after the login.
	                                """;

	/// <summary>The introduction of the documentation of the osu! (stable) client's endpoints.</summary>
	public static readonly ApiDocument Document = new(Group, "Basil osu! (stable) client endpoints", Overview);

	/// <summary>Describes the exchange of packets with the game server.</summary>
	/// <returns>The Markdown description of <c>POST /</c>.</returns>
	public static string ExchangeDescription()
	{
		var text = new StringBuilder();
		text.AppendLine(
			"Without an `osu-token` header the body is the login and the answer carries a `cho-token` header. " +
			"With one, the body is the client's packets and the answer the server's queued packets. " +
			"An unknown token is answered with the packet that restarts the client.").AppendLine();
		text.AppendLine(
				"Each packet is a header (id as 2 bytes, a padding byte, length as 4 bytes, little endian) followed by its payload.")
			.AppendLine();
		Table(text, "Packets the client sends",
			Enum.GetValues<ClientPacketType>().Select(t => (Name: t.ToString(), Id: (int)t)));
		Table(text, "Packets the server sends",
			Enum.GetValues<ServerPacketType>().Select(t => (Name: t.ToString(), Id: (int)t)));
		return text.ToString();
	}

	private static void Table(StringBuilder text, string title, IEnumerable<(string Name, int Id)> packets)
	{
		text.AppendLine($"**{title}**").AppendLine();
		text.AppendLine("| Id | Packet |").AppendLine("| --- | --- |");
		foreach (var (name, id) in packets.OrderBy(packet => packet.Id)) text.AppendLine($"| {id} | `{name}` |");
		text.AppendLine();
	}
}