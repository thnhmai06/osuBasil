using Basil.Application.Diagnostics.Messages;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Infrastructure.Diagnostics.Tests.Handlers;

/// <summary>The requests of the maintenance actions refuse a sender without the privilege and name what is missing.</summary>
public sealed class DiagnosticsRequestTests
{
	[Fact]
	public async Task ClearCaches_FromAUserWithoutThePermission_IsRefusedWithTheMissingMask()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var (result, deleted) = await app.Sender.Send(new ClearCaches(plain), TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Developer, result.Missing);
		Assert.Equal(0, deleted);
	}

	[Fact]
	public async Task ResetExceptions_FromAUserWithoutThePermission_IsRefusedWithTheMissingMask()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Sender.Send(new ResetExceptions(plain), TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Developer, result.Missing);
	}

	[Fact]
	public async Task CollectGarbage_FromAUserWithoutThePermission_IsRefusedWithTheMissingMask()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Sender.Send(new CollectGarbage(plain), TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Developer, result.Missing);
	}

	[Fact]
	public async Task CheckDiagnosticsAccess_FromAUserWithoutThePermission_IsRefusedWithTheMissingMask()
	{
		using var app = new TestApplication();
		var plain = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Sender.Send(new CheckDiagnosticsAccess(plain), TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Developer, result.Missing);
	}

	[Fact]
	public async Task ResetExceptions_FromStaff_IsOk()
	{
		using var app = new TestApplication();
		var staff = await RoomFixture.PlayerAsync(app, "Staff",
			UserPrivilegesExtensions.Default | UserPrivileges.Developer);

		var result = await app.Sender.Send(new ResetExceptions(staff), TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
	}
}