using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>
///     Verifies that the room's mods and freemod setting agree: with freemods on the room mods are only
///     speed-changing mods.
/// </summary>
public class MatchSettingsTests
{
	[Fact]
	public void Mods_FreemodsOffAnyValidMods_Accepted()
	{
		var settings = new MatchSettings { Mods = GameMods.Hidden | GameMods.DoubleTime };

		Assert.Equal(GameMods.Hidden | GameMods.DoubleTime, settings.Mods);
	}

	[Theory]
	[InlineData(GameMods.NoMod)]
	[InlineData(GameMods.DoubleTime)]
	[InlineData(GameMods.HalfTime)]
	[InlineData(GameMods.Nightcore | GameMods.DoubleTime)]
	public void Mods_FreemodsOnSpeedChangingMods_Accepted(GameMods mods)
	{
		var settings = new MatchSettings { Freemods = true };

		settings.Mods = mods;

		Assert.Equal(mods, settings.Mods);
	}

	[Theory]
	[InlineData(GameMods.Hidden)]
	[InlineData(GameMods.DoubleTime | GameMods.Hidden)]
	public void Mods_FreemodsOnOtherMods_Refused(GameMods mods)
	{
		var settings = new MatchSettings { Freemods = true };

		Assert.Throws<ArgumentException>(() => settings.Mods = mods);
		Assert.Equal(GameMods.NoMod, settings.Mods);
	}

	[Fact]
	public void Freemods_OnWithSpeedChangingMods_Accepted()
	{
		var settings = new MatchSettings { Mods = GameMods.DoubleTime };

		settings.Freemods = true;

		Assert.True(settings.Freemods);
	}

	[Fact]
	public void Freemods_OnWithNoMods_Accepted()
	{
		var settings = new MatchSettings();

		settings.Freemods = true;

		Assert.True(settings.Freemods);
	}

	[Fact]
	public void Freemods_OnWithOtherMods_Refused()
	{
		var settings = new MatchSettings { Mods = GameMods.Hidden | GameMods.DoubleTime };

		Assert.Throws<ArgumentException>(() => settings.Freemods = true);
		Assert.False(settings.Freemods);
	}

	[Fact]
	public void Freemods_OffWithOtherMods_Accepted()
	{
		var settings = new MatchSettings { Mods = GameMods.Hidden };

		settings.Freemods = false;

		Assert.False(settings.Freemods);
	}

	[Fact]
	public void Freemods_OffAfterOn_AllowsOtherModsAgain()
	{
		var settings = new MatchSettings { Freemods = true };
		settings.Freemods = false;

		settings.Mods = GameMods.Hidden;

		Assert.Equal(GameMods.Hidden, settings.Mods);
	}

	[Fact]
	public void Initializer_ModsBeforeFreemodsWithSpeedChangingMods_Accepted()
	{
		var settings = new MatchSettings { Mods = GameMods.HalfTime, Freemods = true };

		Assert.True(settings.Freemods);
		Assert.Equal(GameMods.HalfTime, settings.Mods);
	}

	[Fact]
	public void Initializer_FreemodsBeforeOtherMods_Refused()
	{
		Assert.Throws<ArgumentException>(() => new MatchSettings { Freemods = true, Mods = GameMods.Hidden });
	}
}