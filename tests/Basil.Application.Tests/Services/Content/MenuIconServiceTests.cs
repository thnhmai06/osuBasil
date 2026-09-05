using Basil.Application.Abstractions.Settings;
using Basil.Application.Services.Content;
using NSubstitute;

namespace Basil.Application.Tests.Services.Content;

public class MenuIconServiceTests
{
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private MenuIconService MakeService()
	{
		return new MenuIconService(_settings);
	}

	[Fact]
	public async Task SaveUrlAsync_NonEmptyValue_StoresIt()
	{
		await MakeService().SaveUrlAsync("https://example.com");

		await _settings.Received(1)
			.SetAsync("MenuIcon:Url", "https://example.com", Arg.Any<CancellationToken>());
	}

	/// <summary>
	///     Regression test: an empty `url` field on `PUT /menu/icon` used to be silently ignored
	///     (Issue #4) instead of resetting the click-through URL back to unset.
	/// </summary>
	[Fact]
	public async Task SaveUrlAsync_EmptyValue_ClearsToNull()
	{
		await MakeService().SaveUrlAsync("");

		await _settings.Received(1).SetAsync("MenuIcon:Url", null, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SaveUrlAsync_NullValue_ClearsToNull()
	{
		await MakeService().SaveUrlAsync(null);

		await _settings.Received(1).SetAsync("MenuIcon:Url", null, Arg.Any<CancellationToken>());
	}
}
