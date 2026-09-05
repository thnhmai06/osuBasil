using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Authentication;
using NSubstitute;

namespace Basil.Application.Tests.Services.Authentication;

public class AdminKeyServiceTests
{
	private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private AdminKeyService MakeService()
	{
		return new AdminKeyService(_settings, _hasher);
	}

	[Fact]
	public async Task IsBypassAsync_HashUnset_ReturnsTrue()
	{
		_settings.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns((string?)null);

		var result = await MakeService().IsBypassAsync();

		Assert.True(result);
	}

	[Fact]
	public async Task IsBypassAsync_HashSet_ReturnsFalse()
	{
		_settings.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns("some-hash");

		var result = await MakeService().IsBypassAsync();

		Assert.False(result);
	}

	[Fact]
	public async Task VerifyAsync_HashUnset_ReturnsFalse()
	{
		_settings.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns((string?)null);

		var result = await MakeService().VerifyAsync("anything");

		Assert.False(result);
	}

	[Fact]
	public async Task VerifyAsync_MatchingKey_ReturnsTrue()
	{
		_settings.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns("stored-hash");
		_hasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);

		var result = await MakeService().VerifyAsync("correct-key");

		Assert.True(result);
	}

	[Fact]
	public async Task VerifyAsync_WrongKey_ReturnsFalse()
	{
		_settings.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns("stored-hash");
		_hasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);

		var result = await MakeService().VerifyAsync("wrong-key");

		Assert.False(result);
	}

	[Fact]
	public async Task SetKeyAsync_HashesAndStoresHash()
	{
		_hasher.Hash(Arg.Any<byte[]>()).Returns("new-hash");

		await MakeService().SetKeyAsync("new-key");

		await _settings.Received(1).SetAsync("AdminKey:Hash", "new-hash", Arg.Any<CancellationToken>());
	}

	/// <summary>
	///     Regression test: a DB trigger also stamps this column as a safety net for writes that
	///     bypass this service, but that write is invisible to the settings cache decorator, which
	///     only invalidates the key it was actually asked to write. The service must stamp it itself
	///     through the same repository call so the cache invalidates correctly.
	/// </summary>
	[Fact]
	public async Task SetKeyAsync_AlsoStampsLastChanged()
	{
		await MakeService().SetKeyAsync("new-key");

		await _settings.Received(1).SetAsync("AdminKey:LastChanged", Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ClearAsync_WritesNullHash()
	{
		await MakeService().ClearAsync();

		await _settings.Received(1).SetAsync("AdminKey:Hash", null, Arg.Any<CancellationToken>());
	}

	/// <summary>See <see cref="SetKeyAsync_AlsoStampsLastChanged" /> — the same gap applies to clearing the key.</summary>
	[Fact]
	public async Task ClearAsync_AlsoStampsLastChanged()
	{
		await MakeService().ClearAsync();

		await _settings.Received(1).SetAsync("AdminKey:LastChanged", Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetLastChangedAsync_Unset_ReturnsNull()
	{
		_settings.GetAsync("AdminKey:LastChanged", Arg.Any<CancellationToken>()).Returns((string?)null);

		var result = await MakeService().GetLastChangedAsync();

		Assert.Null(result);
	}

	[Fact]
	public async Task GetLastChangedAsync_Set_ReturnsParsedValue()
	{
		var timestamp = DateTimeOffset.UtcNow;
		_settings.GetAsync("AdminKey:LastChanged", Arg.Any<CancellationToken>())
			.Returns(timestamp.ToString("O"));

		var result = await MakeService().GetLastChangedAsync();

		Assert.Equal(timestamp, result);
	}

	[Fact]
	public void MaxKeyLengthBytes_Is72_BCryptTruncationLimit()
	{
		Assert.Equal(72, AdminKeyService.MaxKeyLengthBytes);
	}
}