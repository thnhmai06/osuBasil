using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Basil.Host.Rest.Sse;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>The per-address stream limit holds across roots, under parallel opens, and frees up when streams close.</summary>
[Collection("REST host")]
public sealed class StreamBudgetTests(RestHostFixture fixture)
{
	private static readonly string[] Paths = ["/v1/diagnostics/runtime", "/v1/diagnostics"];

	private static async Task LimitAsync(BasilHost host, int perIp)
	{
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		var current = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(current with
		{
			RateLimits = current.RateLimits with
			{
				Connections = new ServerSettings.ConnectionLimitSettings { PerIp = perIp }
			}
		});
	}

	[Fact]
	public async Task OpenStreams_ParallelOnTwoRoots_ExactlyOneBeyondTheLimitIsRefused()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("BudgetRace", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("BudgetRace");
		await LimitAsync(host, 4);

		var opened = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
			client.OpenStreamAsync(Paths[i % 2], ct: TestContext.Current.CancellationToken)));
		foreach (var (response, _) in opened)
			using (response)
			{
			}

		Assert.Equal(1, opened.Count(open => (int)open.Response.StatusCode == 429));
		Assert.Equal(4, opened.Count(open => (int)open.Response.StatusCode == 200));
	}

	[Fact]
	public async Task OpenStreams_AfterEveryStreamClosed_TheLimitIsAvailableAgain()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("BudgetFree", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("BudgetFree");
		await LimitAsync(host, 2);
		var hub = host.Services.GetRequiredService<SseHub>();

		var (first, _) = await client.OpenStreamAsync(Paths[0], ct: TestContext.Current.CancellationToken);
		var (second, _) = await client.OpenStreamAsync(Paths[1], ct: TestContext.Current.CancellationToken);
		first.Dispose();
		second.Dispose();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		while (hub.OpenStreams != 0)
		{
			timeout.Token.ThrowIfCancellationRequested();
			await Task.Yield();
		}

		var (third, _) = await client.OpenStreamAsync(Paths[0], ct: TestContext.Current.CancellationToken);
		var (fourth, _) = await client.OpenStreamAsync(Paths[1], ct: TestContext.Current.CancellationToken);
		using (third)
		using (fourth)
		{
			Assert.Equal([200, 200], [(int)third.StatusCode, (int)fourth.StatusCode]);
		}
	}
}
