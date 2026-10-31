using System.Text.Json.Nodes;
using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Diagnostics need the permission; a stream carries a full sample first and one per second after.</summary>
[Collection("REST host")]
public sealed class DiagnosticsTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Staff = UserPermissionsGroups.Default |
	                                                UserPermissions.ServerManageDiagnostics;

	[Fact]
	public async Task Diagnostics_WithoutPermission_Answer403_AndWithoutAToken401()
	{
		var (plain, _) = await fixture.SignInAsync("DiagPlain");
		using (plain)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (noToken, _) = await anon.OpenStreamAsync("/v1/diagnostics");
			var (denied, _) = await plain.OpenStreamAsync("/v1/diagnostics");
			var (action, envelope) = await plain.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			using (noToken)
			using (denied)
			{
				Assert.Equal(401, (int)noToken.StatusCode);
				Assert.Equal(403, (int)denied.StatusCode);
			}

			Assert.Equal(403, action);
			Assert.Equal((ulong)UserPermissions.ServerManageDiagnostics, (ulong)envelope!["errors"]!["missing"]!);
		}
	}

	[Fact]
	public async Task Stream_OfEveryCategory_StartsWithAFullSample_AndOneCategoryIsFiltered()
	{
		var (staff, _) = await fixture.SignInAsync("DiagStaff", Staff);
		using (staff)
		{
			var (all, allReader) = await staff.OpenStreamAsync("/v1/diagnostics");
			var (one, oneReader) = await staff.OpenStreamAsync("/v1/diagnostics/application");
			var (unknown, _) = await staff.OpenStreamAsync("/v1/diagnostics/nothing");
			using (all)
			using (one)
			using (unknown)
			{
				var snapshot = JsonNode.Parse((await allReader!.ReadAsync())!.Value.Data)!;
				foreach (var category in new[]
				         {
					         "process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database",
					         "bancho", "irc", "events", "sessions"
				         })
					Assert.NotNull(snapshot[category]);
				var application = JsonNode.Parse((await oneReader!.ReadAsync())!.Value.Data)!;
				Assert.NotNull(application["usersOnline"]);
				Assert.Equal(404, (int)unknown.StatusCode);
			}
		}
	}

	[Fact]
	public async Task Stream_SendsABundleEachSecond()
	{
		var (staff, _) = await fixture.SignInAsync("DiagTick", Staff);
		using (staff)
		{
			var (response, reader) = await staff.OpenStreamAsync("/v1/diagnostics/runtime");
			using (response)
			{
				await reader!.ReadAsync();

				var bundle = await reader.NextBundleAsync(fixture.Host);

				Assert.Equal("bundle", bundle.Event);
				Assert.NotNull(JsonNode.Parse(bundle.Data)![0]!["cpus"]);
			}
		}
	}

	[Fact]
	public async Task Actions_RunForStaff_AndAnUnknownActionIs404()
	{
		var (staff, _) = await fixture.SignInAsync("DiagAction", Staff);
		using (staff)
		{
			var (collect, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/collect");
			var (caches, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/caches/clear");
			var (reset, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/exceptions/reset");
			var (unknown, _) = await staff.CallAsync(HttpMethod.Post, "/v1/diagnostics/gc/explode");

			Assert.Equal([200, 200, 200, 404], [collect, caches, reset, unknown]);
		}
	}
}