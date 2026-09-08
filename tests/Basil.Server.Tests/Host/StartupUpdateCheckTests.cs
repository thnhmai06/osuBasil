using Basil.Server.Host;
using Basil.Server.Shared.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Server.Tests.Host;

/// <summary>Verifies when the server looks for a newer release, and that it never installs one.</summary>
public class StartupUpdateCheckTests
{
	[Fact]
	public async Task TurningTheCheckOffMeansTheReleaseFeedIsNeverContacted()
	{
		var probe = new RecordingProbe(new UpdateCheckResult(UpdateCheckOutcome.UpToDate));
		var check = Build(probe, new UpdateCheckOptions { CheckOnStartup = false });

		await check.StartAsync(TestContext.Current.CancellationToken);
		await check.StopAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, probe.Checks);
	}

	[Fact]
	public async Task AStartupCheckAsksTheFeedExactlyOnce()
	{
		var probe = new RecordingProbe(new UpdateCheckResult(UpdateCheckOutcome.UpToDate));
		var check = Build(probe, new UpdateCheckOptions { CheckOnStartup = true });

		await check.StartAsync(TestContext.Current.CancellationToken);
		await check.StopAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, probe.Checks);
	}

	/// <summary>
	///     Reporting a newer release must not install it: installing is what the update command is
	///     for, and a server that restarts itself mid-tournament is the failure this guards against.
	/// </summary>
	[Fact]
	public async Task FindingANewerReleaseDoesNotInstallIt()
	{
		var probe = new RecordingProbe(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, "9.9.9"));
		var check = Build(probe, new UpdateCheckOptions { CheckOnStartup = true });

		await check.StartAsync(TestContext.Current.CancellationToken);
		await check.StopAsync(TestContext.Current.CancellationToken);

		Assert.Equal(0, probe.Applies);
	}

	[Fact]
	public async Task AFeedThatCannotBeReachedDoesNotStopTheServerFromStarting()
	{
		var probe = new ThrowingProbe();
		var check = Build(probe, new UpdateCheckOptions { CheckOnStartup = true });

		await check.StartAsync(TestContext.Current.CancellationToken);
		await check.StopAsync(TestContext.Current.CancellationToken);
	}

	private static StartupUpdateCheck Build(IUpdateProbe probe, UpdateCheckOptions options)
	{
		return new StartupUpdateCheck(probe, Options.Create(options), NullLogger<StartupUpdateCheck>.Instance);
	}

	private sealed class RecordingProbe(UpdateCheckResult result) : IUpdateProbe
	{
		public int Checks { get; private set; }
		public int Applies { get; private set; }

		public string CurrentVersion => "0.0.0-test";

		public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
		{
			Checks++;
			return Task.FromResult(result);
		}

		public Task<bool> ApplyAsync(CancellationToken cancellationToken)
		{
			Applies++;
			return Task.FromResult(true);
		}
	}

	private sealed class ThrowingProbe : IUpdateProbe
	{
		public string CurrentVersion => "0.0.0-test";

		public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
		{
			throw new HttpRequestException("no network");
		}

		public Task<bool> ApplyAsync(CancellationToken cancellationToken)
		{
			throw new HttpRequestException("no network");
		}
	}
}
