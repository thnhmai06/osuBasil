using Basil.Application.Common.Diagnostics;
using Basil.Application.Diagnostics.Objects;
using Basil.Domain.Users;
using Basil.Testing;
using NSubstitute;

namespace Basil.Application.Diagnostics.Tests.Capabilities;

/// <summary>The diagnostics and their maintenance actions need <see cref="UserPermissions.ServerManageDiagnostics" />.</summary>
public sealed class ScopedDiagnosticsTests
{
	private static readonly UserPermissions Staff =
		UserPermissionsGroups.Default | UserPermissions.ServerManageDiagnostics;

	private readonly ICacheCleaner _caches = Substitute.For<ICacheCleaner>();
	private readonly IClearTally _exceptions = Substitute.For<IClearTally>();

	private ServerDiagnostics Diagnostics(TestApplication app)
	{
		return new ServerDiagnostics(app.Clock, _caches, _exceptions);
	}

	[Fact]
	public async Task Access_WithoutThePermission_IsDeniedAndNamesIt()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).Access();

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ServerManageDiagnostics, result.Missing);
	}

	[Fact]
	public async Task Access_WithThePermission_IsOk()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		Assert.True(Diagnostics(app).ScopeTo(staff).Access().IsOk);
	}

	[Fact]
	public async Task ClearCaches_WithoutThePermission_IsDeniedAndDeletesNothing()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var (result, deleted) = Diagnostics(app).ScopeTo(plain).ClearCaches();

		Assert.Equal(UserPermissions.ServerManageDiagnostics, result.Missing);
		Assert.Equal(0, deleted);
		_caches.DidNotReceive().Clear();
	}

	[Fact]
	public async Task ClearCaches_WithThePermission_ReportsHowManyFilesWereDeleted()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);
		_caches.Clear().Returns(3);

		var (result, deleted) = Diagnostics(app).ScopeTo(staff).ClearCaches();

		Assert.True(result.IsOk);
		Assert.Equal(3, deleted);
	}

	[Fact]
	public async Task ResetExceptions_WithoutThePermission_IsDeniedAndResetsNothing()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).ResetExceptions();

		Assert.Equal(UserPermissions.ServerManageDiagnostics, result.Missing);
		_exceptions.DidNotReceive().Reset();
	}

	[Fact]
	public async Task ResetExceptions_WithThePermission_ResetsTheTally()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		var result = Diagnostics(app).ScopeTo(staff).ResetExceptions();

		Assert.True(result.IsOk);
		_exceptions.Received(1).Reset();
	}

	[Fact]
	public async Task CollectGarbage_WithoutThePermission_IsDenied()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = Diagnostics(app).ScopeTo(plain).CollectGarbage();

		Assert.Equal(UserPermissions.ServerManageDiagnostics, result.Missing);
	}

	[Fact]
	public async Task CollectGarbage_WithThePermission_IsOk()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff", Staff);

		Assert.True(Diagnostics(app).ScopeTo(staff).CollectGarbage().IsOk);
	}
}
