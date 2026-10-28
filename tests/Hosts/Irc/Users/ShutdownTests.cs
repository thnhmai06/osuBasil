using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests.Users;

/// <summary>What clients read when the server stops or restarts.</summary>
[Collection("IRC host")]
public sealed class ShutdownTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Announce_Restart_NoticesThenErrorsThenCloses_AndRefusesNewConnections()
	{
		using var irc = await Host.SignInAsync("ShRestart");

		try
		{
			Host.Services.GetRequiredService<IrcListener>().Announce(true);

			Assert.Equal(":irc.basil.test NOTICE ShRestart :Server is restarting", await irc.ExpectAsync("NOTICE"));
			Assert.Equal("ERROR :Closing Link: basil.test (Server restarting)", await irc.ExpectAsync("ERROR"));
			Assert.Null(await irc.ReadLineAsync());
			using var late = await IrcClient.ConnectAsync(Host);
			Assert.Equal("ERROR :Closing Link: basil.test (The server is restarting. Please reconnect in a moment.)",
				await late.ExpectAsync("ERROR"));
		}
		finally
		{
			Host.Services.GetRequiredService<IrcState>().AcceptingLogins = true;
		}
	}
}
