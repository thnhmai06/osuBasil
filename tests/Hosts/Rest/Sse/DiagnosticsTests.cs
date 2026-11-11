using System.Text.Json.Nodes;
using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Diagnostics need the privilege; a stream carries a full sample first and one per second after.</summary>
[Collection("REST host")]
public sealed class DiagnosticsTests(RestHostFixture fixture)
{
	private static readonly UserPrivileges Staff = UserPrivilegesExtensions.Default |
	                                                UserPrivileges.Developer;

	[Fact]
	public async Task NoPrivilege_403Or401()
	{
		var (plain, _) = await fixture.SignInAsync("DiagPlain");
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (noToken, _) = await anon.OpenStreamAsync("/v1/diagnostics", ct: TestContext.Current.CancellationToken);
			var (denied, _) = await plain.OpenStreamAsync("/v1/diagnostics", ct: TestContext.Current.CancellationToken);
			var (action, envelope) = await plain.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			using (noToken)
			using (denied)
			{
				Assert.Equal(401, (int)noToken.StatusCode);
				Assert.Equal(403, (int)denied.StatusCode);
			}

			Assert.Equal(403, action);
			Assert.Equal("privilegeDenied", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task EveryCategory_FullThenFiltered()
	{
		var (staff, _) = await fixture.SignInAsync("DiagStaff", Staff);
		using (staff)
		{
			var (all, allReader) = await staff.OpenStreamAsync("/v1/diagnostics", ct: TestContext.Current.CancellationToken);
			var (one, oneReader) = await staff.OpenStreamAsync("/v1/diagnostics/application", ct: TestContext.Current.CancellationToken);
			var (unknown, _) = await staff.OpenStreamAsync("/v1/diagnostics/nothing", ct: TestContext.Current.CancellationToken);
			using (all)
			using (one)
			using (unknown)
			{
				var snapshot = JsonNode.Parse((await allReader!.ReadAsync(TestContext.Current.CancellationToken))!.Value.Data)!;
				foreach (var category in new[]
				         {
					         "process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database",
					         "stable", "irc", "events", "sessions"
				         })
					Assert.NotNull(snapshot[category]);
				var application = JsonNode.Parse((await oneReader!.ReadAsync(TestContext.Current.CancellationToken))!.Value.Data)!;
				Assert.NotNull(application["usersOnline"]);
				Assert.Equal(404, (int)unknown.StatusCode);
			}
		}
	}

	[Fact]
	public async Task Stream_BundleEachSecond()
	{
		var (staff, _) = await fixture.SignInAsync("DiagTick", Staff);
		using (staff)
		{
			var (response, reader) = await staff.OpenStreamAsync("/v1/diagnostics/runtime", ct: TestContext.Current.CancellationToken);
			using (response)
			{
				await reader!.ReadAsync(TestContext.Current.CancellationToken);

				var bundle = await reader.NextBundleAsync(fixture.Host);

				Assert.Equal("bundle", bundle.Event);
				Assert.NotNull(JsonNode.Parse(bundle.Data)![0]!["cpus"]);
			}
		}
	}

	[Fact]
	public async Task StaffActions_UnknownIs404()
	{
		var (staff, _) = await fixture.SignInAsync("DiagAction", Staff);
		using (staff)
		{
			// Garbage collection is limited to once in 30 seconds on the server's clock; other tests may have used it.
			fixture.Host.Time.Advance(TimeSpan.FromMinutes(1));
			var (collect, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			var (caches, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/caches/clear");
			var (reset, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/exceptions/reset");
			var (unknown, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/explode");

			Assert.Equal([200, 200, 200, 404], [collect, caches, reset, unknown]);
		}
	}

	[Fact]
	public async Task GcAskedTwice_RefusedThenAllowed()
	{
		var (staff, _) = await fixture.SignInAsync("DiagCollect", Staff);
		using (staff)
		{
			fixture.Host.Time.Advance(TimeSpan.FromMinutes(1));
			var (first, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			var (second, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			fixture.Host.Time.Advance(TimeSpan.FromSeconds(31));
			var (later, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");

			Assert.Equal([200, 429, 200], [first, second, later]);
		}
	}
}