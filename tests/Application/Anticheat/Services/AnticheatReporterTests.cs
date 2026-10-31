using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Anticheat.Tests.Services;

/// <summary>Verifies how the flags a game client reports become anticheat events.</summary>
public sealed class AnticheatReporterTests
{
	[Fact]
	public async Task Report_WithoutCheatSigns_PublishesNothingAndReturnsClean()
	{
		using var app = new TestApplication();
		var player = await LoginAsync(app);
		var reporter = app.Provider.GetRequiredService<IAnticheatReporter>();

		var signs = reporter.Report(player, OsuFlags.Clean);

		Assert.Equal(OsuFlags.Clean, signs);
		Assert.Empty(app.AnticheatEvents.Events);
	}

	[Fact]
	public async Task Report_WithCheatSigns_PublishesOneFlaggedEventNamingThePlayerAndTheSigns()
	{
		using var app = new TestApplication();
		var player = await LoginAsync(app);
		var reporter = app.Provider.GetRequiredService<IAnticheatReporter>();
		const OsuFlags flags = OsuFlags.SpeedHackDetected | OsuFlags.SpinnerHack;

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
		var reporter = app.Provider.GetRequiredService<IAnticheatReporter>();

		var signs = reporter.Report(player, OsuFlags.SpeedHackDetected | OsuFlags.RunWithLdFlag);

		Assert.Equal(OsuFlags.SpeedHackDetected, signs);
		var flagged = Assert.IsType<AnticheatPlayerFlagged>(Assert.Single(app.AnticheatEvents.Events));
		Assert.Equal(OsuFlags.SpeedHackDetected, flagged.Signs);
	}

	private static async Task<BanchoConnection> LoginAsync(TestApplication app)
	{
		var user = await app.CreateUserAsync("Flagged", "password");
		return Assert.IsType<BanchoConnection>(await app.LoginAsync(user, SessionType.Osu));
	}
}