using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests.Support;

/// <summary>Setup shared by the IRC tests: signed-in clients and extra channels.</summary>
internal static class IrcWorld
{
	/// <summary>Creates an account and signs an IRC client in, then waits until it is in <c>#osu</c>.</summary>
	public static async Task<IrcClient> SignInAsync(this BasilHost host, string name, string caps = "",
		UserPermissions? permissions = null)
	{
		var (irc, key) = await host.NewIrcClientAsync(name, permissions);
		var nick = name.Replace(' ', '_');
		if (caps.Length == 0) await irc.RegisterAsync(nick, key);
		else await irc.RegisterWithCapsAsync(nick, key, caps);
		await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "366" && line.Contains(" #osu "));
		return irc;
	}

	/// <summary>Creates an account and logs an osu! client in, then waits until the login reply is read.</summary>
	public static async Task<OsuClient> LoginOsuAsync(this BasilHost host, string name)
	{
		var osu = await host.NewClientAsync(name);
		await osu.LoginAsync();
		return osu;
	}

	/// <summary>Opens a general channel.</summary>
	public static void AddChannel(this BasilHost host, string name, UserPrivileges read = UserPrivileges.None,
		UserPrivileges write = UserPrivileges.None, bool autoJoin = false, string topic = "test topic",
		bool visible = true)
	{
		host.Services.GetRequiredService<Basil.Application.Servers.Runtime.Server>().OpenChannel(new GeneralChannelRecord
		{
			Name = name,
			Value = new GeneralChannelData
			{
				AutoJoin = autoJoin, Topic = topic, ReadPrivileges = read, WritePrivileges = write, Visible = visible
			}
		});
	}

	/// <summary>Waits until a line of the given command arrives for the client and returns it.</summary>
	public static Task<string> ExpectFromAsync(this IrcClient irc, string command, string containing)
	{
		return irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == command && line.Contains(containing));
	}
}
