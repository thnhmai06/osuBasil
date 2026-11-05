using Basil.Application.Content.Persistence;
using Basil.Host.Irc.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests;

/// <summary>A client that sends lines faster than the gateway allows is told so and disconnected.</summary>
[Collection("IRC host")]
public sealed class FloodTests(IrcHostFixture fixture)
{
	[Fact]
	public async Task Lines_BeyondTheAllowance_AreAnsweredWithANotice_ThenTheConnectionIsClosed()
	{
		var settings = fixture.Host.Services.GetRequiredService<ISettingsRepository>();
		var original = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(original with
		{
			RateLimits = original.RateLimits with
			{
				Admission = new ServerSettings.AdmissionLimitSettings(2, TimeSpan.FromMinutes(1), 4, 256, 32, 8, 1024,
					0.5, 0.7, 0.9, TimeSpan.FromSeconds(5))
			}
		});

		try
		{
			using var client = await IrcClient.ConnectAsync(fixture.Host);
			await client.SendAsync("PING one");
			await client.SendAsync("PING two");
			await client.SendAsync("PING three");

			var notice = await client.ExpectAsync("NOTICE");
			var closing = await client.ExpectAsync("ERROR");

			Assert.EndsWith($":{IrcReplies.Flooding}", notice);
			Assert.Contains($"({IrcReplies.Flooding})", closing);
		}
		finally
		{
			await settings.CreateOrUpdateAsync(original);
		}
	}
}
