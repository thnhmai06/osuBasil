using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Game.Users;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests.Support;

internal static class IrcWorld
{
	extension(BasilHost host)
	{
		public async Task<IrcClient> SignInAsync(string name, string caps = "",
			UserPrivileges? privileges = null)
		{
			var (irc, key) = await host.NewIrcClientAsync(name, privileges);
			var nick = name.Replace(' ', '_');
			if (caps.Length == 0) await irc.RegisterAsync(nick, key);
			else await irc.RegisterWithCapsAsync(nick, key, caps);
			await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "366" && line.Contains(" #osu "));
			return irc;
		}

		public async Task<OsuClient> LoginOsuAsync(string name)
		{
			var osu = await host.NewClientAsync(name);
			await osu.LoginAsync();
			return osu;
		}

		public void AddChannel(string name, UserPrivileges read = UserPrivileges.None,
			UserPrivileges write = UserPrivileges.None, bool autoJoin = false, string topic = "test topic",
			bool visible = true)
		{
			host.Services.GetRequiredService<Server>().OpenChannel(new GeneralChannelRecord
			{
				Name = name,
				Value = new GeneralChannelData
				{
					AutoJoin = autoJoin, Topic = topic, ReadPrivileges = read, WritePrivileges = write, Visible = visible
				}
			});
		}
	}

	extension(IrcClient irc)
	{
		public async Task<IReadOnlyList<string>> NamesOfAsync(string channel)
		{
			await irc.SendAsync($"NAMES {channel}");
			var lines = await irc.BarrierAsync();
			return
			[
				.. lines.Where(line => IrcClient.CommandOf(line) == "353" && line.Contains($" {channel} :"))
					.SelectMany(line => line.Split(" :", 2)[1].Split(' '))
			];
		}

		public Task<string> ExpectFromAsync(string command, string containing)
		{
			return irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == command && line.Contains(containing));
		}
	}
}