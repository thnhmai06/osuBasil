using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Bancho;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Runtime.Tests.Anticheat;

/// <summary>Verifies how the flags a game client reports become anticheat events.</summary>
public sealed class AnticheatReporterTests
{
	[Fact]
	public async Task Report_WithoutCheatSigns_PublishesNothingAndReturnsClean()
	{
		using var app = new TestApplication();
		var player = await LoginAsync(app);
		var reporter = app.Provider.GetRequiredService<AnticheatReporter>();

		var signs = reporter.Report(player, BanchoFlags.Clean);

		Assert.Equal(BanchoFlags.Clean, signs);
		Assert.Empty(app.AnticheatEvents.Events);
	}

	[Fact]
	public async Task Report_WithCheatSigns_PublishesOneFlaggedEventNamingThePlayerAndTheSigns()
	{
		using var app = new TestApplication();
		var player = await LoginAsync(app);
		var reporter = app.Provider.GetRequiredService<AnticheatReporter>();
		const BanchoFlags flags = BanchoFlags.SpeedHackDetected | BanchoFlags.SpinnerHack;

		var signs = reporter.Report(player, flags);

		Assert.Equal(flags, signs);
		var flagged = Assert.IsType<AnticheatPlayerFlagged>(Assert.Single(app.AnticheatEvents.Events));
		Assert.Same(player, flagged.Player);
		Assert.Equal(flags, flagged.Signs);
	}

	[Fact]
	public async Task Report_FlagsOutsideCheatSigns_PublishesOnlyTheSignBits()
	{
		using var app = new TestApplication();
		var player = await LoginAsync(app);
		var reporter = app.Provider.GetRequiredService<AnticheatReporter>();

		var signs = reporter.Report(player, BanchoFlags.SpeedHackDetected | BanchoFlags.RunWithLdFlag);

		Assert.Equal(BanchoFlags.SpeedHackDetected, signs);
		var flagged = Assert.IsType<AnticheatPlayerFlagged>(Assert.Single(app.AnticheatEvents.Events));
		Assert.Equal(BanchoFlags.SpeedHackDetected, flagged.Signs);
	}

	private static async Task<BanchoConnection> LoginAsync(TestApplication app)
	{
		var user = await app.CreateUserAsync("Flagged", "password");
		return Assert.IsType<BanchoConnection>(await app.LoginAsync(user, ConnectionType.Bancho));
	}
}
