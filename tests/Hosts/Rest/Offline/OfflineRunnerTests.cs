using Basil.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Rest.Tests.Offline;

/// <summary>The offline command that creates the first administrator, which CI uses before the server starts.</summary>
[Collection("REST host")]
public sealed class OfflineRunnerTests(RestHostFixture fixture)
{
	private static Dictionary<string, string?> Settings(string connection, string root) => new()
	{
		["Basil:Database:ConnectionString"] = connection,
		["Basil:Storage:DataDirectory"] = Path.Combine(root, "Data"),
		["Basil:Server:Domain"] = BasilHost.DomainName,
		["Basil:Server:Ports:Irc"] = "0",
		["Basil:Bot:Replies:Path"] = Path.Combine(root, "Templates", "replies.json")
	};

	[Fact]
	public async Task UsersCreate_StoresAnAdministrator_WhoCanThenSignInToTheServer_AndATakenNameFails()
	{
		var name = "basil-offline-" + Guid.NewGuid().ToString("N");
		var connection = await fixture.Database.CreateDatabaseAsync(name);
		var root = Path.Combine(Path.GetTempPath(), name);
		try
		{
			var args = new[] { "users", "create", "--username", "FirstAdmin", "--password", "s3cret", "--admin" };

			var created = await OfflineRunner.RunAsync(args, b => b.Configuration.AddInMemoryCollection(Settings(connection, root)));
			var again = await OfflineRunner.RunAsync(args, b => b.Configuration.AddInMemoryCollection(Settings(connection, root)));
			var missing = await OfflineRunner.RunAsync(["users", "create", "--username", "NoPassword"],
				b => b.Configuration.AddInMemoryCollection(Settings(connection, root)));

			Assert.Equal(0, created);
			Assert.Equal(1, again);
			Assert.NotEqual(0, missing);

			var app = HostApplication.Create([], builder =>
			{
				builder.Configuration.AddInMemoryCollection(Settings(connection, root));
				builder.WebHost.UseUrls("http://127.0.0.1:0");
				builder.Logging.ClearProviders();
			});
			await app.StartAsync();
			try
			{
				var url = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
				using var client = new RestClient(url, BasilHost.DomainName);
				var id = await client.LoginAsync("FirstAdmin", "s3cret");
				var (status, user) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}");

				Assert.Equal(200, status);
				Assert.Equal((ulong)UserPermissionsGroups.All, (ulong)user!["data"]!["permissions"]!);
			}
			finally
			{
				await app.StopAsync();
				await app.DisposeAsync();
			}
		}
		finally
		{
			await fixture.Database.DropDatabaseAsync(name);
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}
}
