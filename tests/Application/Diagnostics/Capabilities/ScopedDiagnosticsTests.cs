using Basil.Application.Diagnostics.Services;
using Basil.Application.Diagnostics.Objects;
using Basil.Domain.Users;
using Basil.Testing;
using NSubstitute;

namespace Basil.Application.Diagnostics.Tests.Capabilities;

/// <summary>The diagnostics and their maintenance actions need <see cref="UserPrivileges.Developer" />.</summary>
public sealed class ScopedDiagnosticsTests
{
	private static readonly UserPrivileges Staff =
		UserPrivilegesExtensions.Default | UserPrivileges.Developer;

	private readonly ICacheCleaner _caches = Substitute.For<ICacheCleaner>();
	private readonly IClearTally _exceptions = Substitute.For<IClearTally>();

	private ServerDiagnostics Diagnostics(TestApplication app)
	{
		return new ServerDiagnostics(app.Clock, _caches, _exceptions);
	}

	[Fact]
	public async Task AccessNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).Access();

		Assert.False(result == DiagnosticsOutcome.Ok);
		Assert.Equal(DiagnosticsOutcome.Denied, result);
	}

	[Fact]
	public async Task AccessPermitted_Ok()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		Assert.True(Diagnostics(app).ScopeTo(staff).Access() == DiagnosticsOutcome.Ok);
	}

	[Fact]
	public async Task ClearNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var (result, deleted) = Diagnostics(app).ScopeTo(plain).ClearCaches();

		Assert.Equal(DiagnosticsOutcome.Denied, result);
		Assert.Equal(0, deleted);
		_caches.DidNotReceive().Clear();
	}

	[Fact]
	public async Task ClearPermitted_CountReported()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);
		_caches.Clear().Returns(3);

		var (result, deleted) = Diagnostics(app).ScopeTo(staff).ClearCaches();

		Assert.True(result == DiagnosticsOutcome.Ok);
		Assert.Equal(3, deleted);
	}

	[Fact]
	public async Task ResetNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).ResetExceptions();

		Assert.Equal(DiagnosticsOutcome.Denied, result);
		_exceptions.DidNotReceive().Reset();
	}

	[Fact]
	public async Task ResetPermitted_Reset()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		var result = Diagnostics(app).ScopeTo(staff).ResetExceptions();

		Assert.True(result == DiagnosticsOutcome.Ok);
		_exceptions.Received(1).Reset();
	}

	[Fact]
	public async Task CollectNoPrivilege_Denied()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).CollectGarbage();

		Assert.Equal(DiagnosticsOutcome.Denied, result);
	}

	[Fact]
	public async Task CollectPermitted_Ok()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		Assert.True(Diagnostics(app).ScopeTo(staff).CollectGarbage() == DiagnosticsOutcome.Ok);
	}
}