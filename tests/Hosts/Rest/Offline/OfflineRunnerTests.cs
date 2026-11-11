using Basil.Domain.Users;
using Basil.Lifetime;
using Basil.Testing.Persistence;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Rest.Tests.Offline;

/// <summary>The offline command that creates the first administrator, which CI uses before the server starts.</summary>
[Collection("REST host")]
public sealed class OfflineRunnerTests(RestHostFixture fixture)
{
	private static Dictionary<string, string?> Settings(string connection, string root)
	{
		return new Dictionary<string, string?>
		{
			["ConnectionStrings:Write"] = connection,
			["Basil:Database:Write:Provider"] = TestServer.ProviderName,
			["ConnectionStrings:Read"] = "",
			["Basil:Storage:Location"] = Path.Combine(root, "Data"),
			["Basil:Server:Domain"] = BasilHost.DomainName,
			["Basil:Server:Irc:Port"] = "0",
			["Kestrel:Endpoints:Https:Url"] = "http://127.0.0.1:0",
			["Kestrel:Certificates:Default:Path"] = "",
			["Basil:Bot:Replies:Path"] = Path.Combine(root, "Templates", "replies.json")
		};
	}

	[Fact]
	public async Task CreateAdmin_LoginAndRefusal()
	{
		var name = "basil-offline-" + Guid.NewGuid().ToString("N");
		var connection = await fixture.Database.CreateDatabaseAsync(name);
		var root = Path.Combine(Path.GetTempPath(), name);
		try
		{
			var args = new[] { "users", "create", "--username", "FirstAdmin", "--password", "s3cret", "--admin" };

			var text = new StringWriter();
			var created = await ShellRunner.RunAsync(args,
				b => b.Configuration.AddInMemoryCollection(Settings(connection, root)), text,
				ct: TestContext.Current.CancellationToken);
			var again = await OfflineRunner.RunAsync(args,
				b => b.Configuration.AddInMemoryCollection(Settings(connection, root)));
			var missing = await OfflineRunner.RunAsync(["users", "create", "--username", "NoPassword"],
				b => b.Configuration.AddInMemoryCollection(Settings(connection, root)));

			Assert.True(created == 0, text.ToString());
			Assert.Equal(1, again);
			Assert.NotEqual(0, missing);

			var app = HostApplication.Create([], builder =>
			{
				builder.Configuration.AddInMemoryCollection(Settings(connection, root));
				builder.Logging.ClearProviders();
			});
			await app.StartAsync(TestContext.Current.CancellationToken);
			try
			{
				var url = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
					.Addresses.First());
				using var client = new RestClient(url, BasilHost.DomainName);
				var id = await client.LoginAsync("FirstAdmin", "s3cret");
				var (status, user) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{id}");

				Assert.Equal(200, status);
				Assert.Equal((ulong)UserPrivilegesExtensions.All, (ulong)user!["data"]!["privileges"]!);
			}
			finally
			{
				await app.StopAsync(TestContext.Current.CancellationToken);
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