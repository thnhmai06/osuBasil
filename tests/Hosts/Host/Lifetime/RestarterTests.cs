using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Tests.Lifetime;

/// <summary>A restarted server waits for the ports the replaced process still holds, but only for a while.</summary>
public sealed class RestarterTests
{
	[Fact]
	public async Task FreePorts_AreNotWaitedFor()
	{
		var clock = new FakeTimeProvider();

		Assert.True(await Restarter.WaitForPortsAsync(() => true, clock, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task APortThatIsFreedLater_IsWaitedFor()
	{
		var clock = new FakeTimeProvider();
		var checks = 0;

		var waiting = Restarter.WaitForPortsAsync(() => ++checks > 3, clock, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		while (!waiting.IsCompleted) clock.Advance(TimeSpan.FromMilliseconds(250));

		Assert.True(await waiting);
	}

	[Fact]
	public async Task APortThatStaysTaken_IsGivenUpOnAfterTheTimeout()
	{
		var clock = new FakeTimeProvider();

		var waiting = Restarter.WaitForPortsAsync(() => false, clock, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		while (!waiting.IsCompleted) clock.Advance(TimeSpan.FromMilliseconds(250));

		Assert.False(await waiting);
		Assert.True(clock.GetUtcNow() - new FakeTimeProvider().GetUtcNow() <= TimeSpan.FromSeconds(10.5));
	}

	[Fact]
	public void APortCanBeBoundOnlyWhenNobodyHoldsIt()
	{
		using var holder = new TcpListener(IPAddress.Loopback, 0);
		holder.Start();
		var port = ((IPEndPoint)holder.LocalEndpoint).Port;

		Assert.False(Restarter.CanBind(IPAddress.Loopback, port));
		holder.Stop();
		Assert.True(Restarter.CanBind(IPAddress.Loopback, port));
	}

	[Fact]
	public void PortZero_IsAlwaysFree()
	{
		Assert.True(Restarter.CanBind(IPAddress.Loopback, 0));
	}

	[Fact]
	public void TheRestartExitCodeIs75()
	{
		Assert.Equal(75, Basil.Host.Cli.Shutdown.ProcessExitCodes.Restart);
	}
}
