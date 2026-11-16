using System.Reflection;
using Basil.Domain.Authentication;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Tabs;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>Wheel acceleration, the exceptions of the last minute, the labels of the settings and the search of the sessions.</summary>
public sealed class SmallPartsTests
{
	[Fact]
	public void Wheel_QuickNotches_GrowTheStep()
	{
		var clock = new FakeTimeProvider();
		var wheel = new WheelAcceleration(clock, 3);
		var first = wheel.Step();

		var last = first;
		for (var i = 0; i < 8; i++)
		{
			clock.Advance(TimeSpan.FromMilliseconds(10));
			last = wheel.Step();
		}

		Assert.Equal(3, first);
		Assert.True(last > first);
	}

	[Fact]
	public void Wheel_APause_ResetsTheStep()
	{
		var clock = new FakeTimeProvider();
		var wheel = new WheelAcceleration(clock, 3);
		for (var i = 0; i < 9; i++)
		{
			clock.Advance(TimeSpan.FromMilliseconds(10));
			wheel.Step();
		}

		clock.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal(3, wheel.Step());
	}

	[Fact]
	public void Exceptions_GrowthOfTheTotal_IsMergedByType()
	{
		var window = new ExceptionWindow();
		var now = new FakeTimeProvider().GetUtcNow();
		window.Update(5, [], now);

		var counts = window.Update(8, ["IOException", "IOException", "TimeoutException"], now);

		Assert.Equal([new ExceptionCount("IOException", 2), new ExceptionCount("TimeoutException", 1)], counts);
	}

	[Fact]
	public void Exceptions_ShowTheTenMostFrequentTypes()
	{
		var window = new ExceptionWindow();
		var now = new FakeTimeProvider().GetUtcNow();
		window.Update(0, [], now);

		var counts = window.Update(12, [.. Enumerable.Range(0, 12).Select(i => $"Type{i:00}")], now);

		Assert.Equal(10, counts.Count);
	}

	[Fact]
	public void Exceptions_OlderThanAMinute_AreDropped()
	{
		var clock = new FakeTimeProvider();
		var window = new ExceptionWindow();
		window.Update(0, [], clock.GetUtcNow());
		window.Update(1, ["IOException"], clock.GetUtcNow());

		clock.Advance(TimeSpan.FromSeconds(59));
		var still = window.Update(1, [], clock.GetUtcNow());
		clock.Advance(TimeSpan.FromSeconds(2));
		var gone = window.Update(1, [], clock.GetUtcNow());

		Assert.Single(still);
		Assert.Empty(gone);
	}

	[Fact]
	public void Words_Humanise_WritesCamelCaseAsWords()
	{
		Assert.Equal("Rate limits", Words.Humanise("rateLimits"));
	}

	[Theory]
	[InlineData("Login: lockout time", "Login", "Basil", "Lockout time")]
	[InlineData("Login: lockout time", "Other", "Login", "Lockout time")]
	[InlineData("Other: lockout time", "Login", "Basil", "Other: lockout time")]
	[InlineData("Lockout time", "Login", "Basil", "Lockout time")]
	public void SettingsLabel_DropsTheNameOfItsOwnGroup(string label, string group, string category, string expected)
	{
		var relative = typeof(SettingsTab).GetMethod("Relative", BindingFlags.Static | BindingFlags.NonPublic)!;

		Assert.Equal(expected, relative.Invoke(null, [label, group, category]));
	}

	private static SessionItem Session(string name, int id, string ip, string client, SessionType type)
	{
		var now = new FakeTimeProvider().GetUtcNow();
		return new SessionItem(1, id, name, 0, type, ip, client, now, now, true, string.Empty);
	}

	private static bool Matches(SessionItem item, string text)
	{
		return (bool)typeof(SessionsSource).GetMethod("Matches", BindingFlags.Static | BindingFlags.NonPublic)!
			.Invoke(null, [item, text])!;
	}

	[Theory]
	[InlineData("", true)]
	[InlineData("pepp", true)]
	[InlineData("PEPPY", true)]
	[InlineData("2", true)]
	[InlineData("10.0.", true)]
	[InlineData("lazer", true)]
	[InlineData("tourney", true)]
	[InlineData("nobody", false)]
	[InlineData("22", false)]
	public void SessionsSearch_FiltersByNameIdAddressClientAndKind(string text, bool expected)
	{
		var item = Session("peppy", 2, "10.0.0.1", "lazer 2025", SessionType.StableTourney);

		Assert.Equal(expected, Matches(item, text));
	}
}
