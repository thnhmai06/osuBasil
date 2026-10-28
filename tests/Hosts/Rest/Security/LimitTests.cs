using System.Net;
using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Basil.Host.Rest.Sse;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Security;

/// <summary>Rate limits, stream limits, CORS and shutdown: the settings are read at run time.</summary>
[Collection("REST host")]
public sealed class LimitTests(RestHostFixture fixture)
{
	private static async Task ChangeAsync(BasilHost host, Func<ServerSettings, ServerSettings> change)
	{
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		await settings.CreateOrUpdateAsync(change(await settings.GetAsync()));
	}

	[Fact]
	public async Task Mutations_BeyondTheLimitOfAUser_Answer429WithRetryAfter_InTheEnvelope()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("LimitMut");
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("LimitMut");
		await ChangeAsync(host, s => s with
		{
			RateLimits = s.RateLimits with
			{
				Rest = s.RateLimits.Rest with { Mutations = new ServerSettings.Limit(2, TimeSpan.FromMinutes(1)) }
			}
		});

		var statuses = new List<int>();
		for (var i = 0; i < 3; i++) statuses.Add((await client.CallAsync(HttpMethod.Patch, "/v1/users/1", new { away = "x" })).Status);
		using var limited = await client.SendAsync(HttpMethod.Patch, "/v1/users/1", new { away = "x" });
		var body = await limited.Content.ReadAsStringAsync();

		Assert.NotEqual(429, statuses[0]);
		Assert.NotEqual(429, statuses[1]);
		Assert.Equal(429, statuses[2]);
		Assert.Equal(429, (int)limited.StatusCode);
		Assert.Equal("60", limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ToString("0"));
		Assert.Contains("rateLimited", body);
	}

	[Fact]
	public async Task AnonymousRequests_BeyondTheLimitOfAnAddress_Answer429_ButATokenIsNotCounted() // the login is anonymous too, so 3 allowed = login + 2
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("LimitAnon");
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("LimitAnon");
		await ChangeAsync(host, s => s with
		{
			RateLimits = s.RateLimits with
			{
				Rest = s.RateLimits.Rest with
				{
					Anonymous = s.RateLimits.Rest.Anonymous with { PerIp = new ServerSettings.Limit(3, TimeSpan.FromMinutes(1)) }
				}
			}
		});
		using var anon = new RestClient(host.Url, host.Domain);

		var statuses = new List<int>();
		for (var i = 0; i < 3; i++) statuses.Add((await anon.CallAsync(HttpMethod.Get, "/v1/users/1")).Status);
		var withToken = (await client.CallAsync(HttpMethod.Get, "/v1/users/1")).Status;

		Assert.Equal([200, 200, 429], statuses);
		Assert.Equal(200, withToken);
	}

	[Fact]
	public async Task OpenStreams_BeyondTheLimitPerAddress_Answer429()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("LimitStream", "password", UserPermissionsGroups.Default | UserPermissions.ServerManageDiagnostics);
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("LimitStream");
		await ChangeAsync(host, s => s with
		{
			RateLimits = s.RateLimits with { Connections = new ServerSettings.ConnectionLimitSettings { PerIp = 2 } }
		});

		var (first, _) = await client.OpenStreamAsync("/v1/diagnostics/runtime");
		var (second, _) = await client.OpenStreamAsync("/v1/diagnostics/runtime");
		var (third, _) = await client.OpenStreamAsync("/v1/diagnostics/runtime");
		using (first)
		using (second)
		using (third)
			Assert.Equal([200, 200, 429], [(int)first.StatusCode, (int)second.StatusCode, (int)third.StatusCode]);
	}

	[Fact]
	public async Task Shutdown_SendsAFinalServerEvent_EndsStreams_AndRefusesNewOnesWith503()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		await host.CreateUserAsync("LimitStop", "password", UserPermissionsGroups.Default | UserPermissions.ServerManageDiagnostics);
		using var client = new RestClient(host.Url, host.Domain);
		await client.LoginAsync("LimitStop");
		var (response, reader) = await client.OpenStreamAsync("/v1/diagnostics/runtime");
		using (response)
		{
			await reader!.ReadAsync();

			await host.Services.GetRequiredService<SseHub>().StopAsync(CancellationToken.None);
			var final = await reader.ReadAsync();
			var end = await reader.ReadAsync();
			var (late, _) = await client.OpenStreamAsync("/v1/diagnostics/runtime");
			using var lateResponse = late;

			Assert.Equal("server", final!.Value.Event);
			Assert.Null(end);
			Assert.Equal(503, (int)late.StatusCode);
			Assert.NotNull(late.Headers.RetryAfter);
		}
	}

	[Fact]
	public async Task Cors_ReadsTheAllowedOriginsFromTheSettingsOnEveryRequest()
	{
		await using var host = await BasilHost.StartAsync(fixture.Database);
		using var http = new HttpClient { BaseAddress = host.Url };

		async Task<(string? Origin, string? Headers)> AskAsync(string origin)
		{
			using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/rooms");
			request.Headers.Host = $"api.{host.Domain}";
			request.Headers.Add("Origin", origin);
			request.Headers.Add("Access-Control-Request-Method", "GET");
			request.Headers.Add("Access-Control-Request-Headers", "authorization");
			using var response = await http.SendAsync(request);
			return (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var o) ? o.Single() : null,
				response.Headers.TryGetValues("Access-Control-Allow-Headers", out var h) ? string.Join(",", h) : null);
		}

		var before = await AskAsync("https://overlay.example");
		await ChangeAsync(host, s => s with { Cors = new ServerSettings.CorsSettings { AllowedOrigins = ["https://overlay.example"] } });
		var allowed = await AskAsync("https://overlay.example");
		var other = await AskAsync("https://evil.example");

		Assert.Null(before.Origin);
		Assert.Equal("https://overlay.example", allowed.Origin);
		Assert.Contains("authorization", allowed.Headers, StringComparison.OrdinalIgnoreCase);
		Assert.Null(other.Origin);
	}
}
