using System.Text.Json.Nodes;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>The HTTP diagnostics stream and the dashboard read the same sample of the same server.</summary>
[Collection("Host end to end")]
public sealed class DiagnosticsEndToEndTests(HostEndToEndFixture fixture)
{
	private static readonly UserPermissions Staff =
		UserPermissionsGroups.Default | UserPermissions.ServerManageDiagnostics;

	[Fact]
	public async Task TheDiagnosticsStream_AndTheDashboard_ReportTheSameCounts()
	{
		await fixture.Host.CreateUserAsync("E2eDiag", "password", Staff);
		using var rest = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		await rest.LoginAsync("E2eDiag");
		var (response, reader) = await rest.OpenStreamAsync("/v1/diagnostics/application");
		using (response)
		{
			Assert.NotNull(reader);
			var snapshot = JsonNode.Parse((await reader.ReadAsync())!.Value.Data)!;
			var model = fixture.Host.Services.GetRequiredService<DashboardModel>();

			fixture.Host.Services.GetRequiredService<DashboardSampler>().Sample();

			Assert.Equal((int)snapshot["usersOnline"]!, (int)model.UsersOnline.Latest);
			Assert.Equal((int)snapshot["rooms"]!, (int)model.Rooms.Latest);
		}
	}
}