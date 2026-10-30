using System.Text.Json.Nodes;
using Basil.Application.Multiplayer.Runtime;
using Basil.Host.Cli.Shutdown;
using Basil.Host.Irc.Support;
using Basil.Host.Lifetime;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>
///     A graceful shutdown as the operating system asks for it: new work is refused with 503 and a
///     <c>Retry-After</c>, every client is told in its own protocol, the osu! packets are taken by one more poll,
///     the rooms close, and the queues drain before the listeners go.
/// </summary>
[Collection("Host end to end")]
public sealed class ShutdownEndToEndTests(HostEndToEndFixture fixture)
{
	private async Task<BasilHost> StartAsync()
	{
		var host = await BasilHost.StartAsync(fixture.Database);
		host.OpenChannel("#osu");
		return host;
	}

	private static GracefulShutdown ShutdownOf(BasilHost host)
	{
		return host.Services.GetRequiredService<IEnumerable<IHostedService>>().OfType<GracefulShutdown>().Single();
	}

	private static async Task<(IrcClient Client, string Key)> RegisteredIrcAsync(BasilHost host, string name)
	{
		var (irc, key) = await host.NewIrcClientAsync(name);
		await irc.RegisterAsync(name, key);
		await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "366" && line.Contains(" #osu "));
		irc.Clear();
		return (irc, key);
	}

	[Fact]
	public async Task Stopping_TheHost_LeavesNoBackgroundServiceFaulted()
	{
		await using var host = await StartAsync();
		var (irc, _) = await RegisteredIrcAsync(host, "E2eStopQuiet");
		using var ircClient = irc;

		await host.StopAsync();

		var faulted = host.Services.GetRequiredService<IEnumerable<IHostedService>>().OfType<BackgroundService>()
			.Where(service => service.ExecuteTask is { IsFaulted: true }).Select(service => service.GetType().Name);
		Assert.Empty(faulted);
	}

	[Fact]
	public async Task Shutdown_RefusesNewWork_TellsEveryClient_ClosesRooms_AndDrains()
	{
		await using var host = await StartAsync();
		using var osu = await host.NewClientAsync("E2eStopOsu");
		await osu.LoginAsync();
		var (irc, _) = await RegisteredIrcAsync(host, "E2eStopIrc");
		using var ircClient = irc;
		await host.CreateUserAsync("E2eStopRest");
		using var rest = new RestClient(host.Url, host.Domain);
		await rest.LoginAsync("E2eStopRest");
		var (_, room) = await rest.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "stopping room" });
		var roomId = (int)room!["data"]!["id"]!;
		var (roomResponse, roomReader) = await rest.OpenStreamAsync($"/v1/rooms/{roomId}/live");
		await roomReader!.ReadAsync();

		host.Services.GetRequiredService<ShutdownController>().Terminate();
		var shutdown = ShutdownOf(host);
		var running = shutdown.StoppingAsync(CancellationToken.None);

		using var refused = await rest.SendAsync(HttpMethod.Get, "/v1/auth/session");
		await host.CreateUserAsync("E2eStopLate");
		using var lateOsu = OsuClient.Create(host, "E2eStopLate", "password");
		await lateOsu.LoginAsync();
		var (lateIrc, _) = await host.NewIrcClientAsync("E2eStopLateIrc");
		using var lateIrcClient = lateIrc;
		var lateIrcError = await lateIrc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "ERROR");
		var polled = await osu.PollAsync();
		var notice = await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "NOTICE");
		var error = await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "ERROR");
		(string Event, string Data) final = default;
		while (await roomReader.NextOrEndAsync(host) is { } next)
			if (next.Event == "server")
			{
				final = next;
				break;
			}

		await running;
		roomResponse.Dispose();

		Assert.Equal(503, (int)refused.StatusCode);
		Assert.Equal(TimeSpan.FromSeconds(5), refused.Headers.RetryAfter!.Delta);
		Assert.True(lateOsu.LoginCode < 0);
		Assert.Contains(IrcReplies.ServerStopping, lateIrcError);
		Assert.Contains(polled, packet => packet.Id == OsuClient.In.Restart && packet.AsInt() == 0);
		Assert.EndsWith(IrcReplies.ShuttingDownNotice, notice);
		Assert.Contains(IrcReplies.ShuttingDown, error);
		Assert.Equal("server", final.Event);
		Assert.Equal("shuttingDown", (string)JsonNode.Parse(final.Data)!["state"]!);
		Assert.All(shutdown.Results, result => Assert.Equal(ShutdownStepOutcome.Completed, result.Outcome));
		Assert.Empty(host.Services.GetRequiredService<Lobby>().Rooms);
	}

	[Fact]
	public async Task Restart_TellsEveryClientThatTheServerComesBack()
	{
		await using var host = await StartAsync();
		using var osu = await host.NewClientAsync("E2eRestartOsu");
		await osu.LoginAsync();
		var (irc, _) = await RegisteredIrcAsync(host, "E2eRestartIrc");
		using var ircClient = irc;
		host.Services.GetRequiredService<ShutdownController>().RequestShutdown(true);
		var shutdown = ShutdownOf(host);

		var running = shutdown.StoppingAsync(CancellationToken.None);
		var polled = await osu.PollAsync();
		var notice = await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "NOTICE");
		var error = await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "ERROR");
		await running;

		Assert.Contains(polled, packet => packet.Id == OsuClient.In.Restart);
		Assert.EndsWith(IrcReplies.RestartingNotice, notice);
		Assert.Contains(IrcReplies.Restarting, error);
		Assert.All(shutdown.Results, result => Assert.Equal(ShutdownStepOutcome.Completed, result.Outcome));
	}

	[Fact]
	public async Task TheSseStream_EndsWithAServerEventThatSaysTheServerRestarts()
	{
		await using var host = await StartAsync();
		await host.CreateUserAsync("E2eRestartRest");
		using var rest = new RestClient(host.Url, host.Domain);
		await rest.LoginAsync("E2eRestartRest");
		var (_, room) = await rest.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "restarting room" });
		var roomId = (int)room!["data"]!["id"]!;
		var (response, reader) = await rest.OpenStreamAsync($"/v1/rooms/{roomId}/live");
		await reader!.ReadAsync();
		host.Services.GetRequiredService<ShutdownController>().RequestShutdown(true);

		await ShutdownOf(host).StoppingAsync(CancellationToken.None);
		(string Event, string Data) final = default;
		while (await reader.NextOrEndAsync(host) is { } next)
			if (next.Event == "server")
			{
				final = next;
				break;
			}

		response.Dispose();

		Assert.Equal("server", final.Event);
		Assert.Equal("restarting", (string)JsonNode.Parse(final.Data)!["state"]!);
	}
}
