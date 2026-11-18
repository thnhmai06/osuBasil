using Basil.Domain.Game.Client;
using Basil.Domain.Game.Spectating;

namespace Basil.Domain.Tests.Client;

public class SpectatorButtonValuesTests
{
	public static TheoryData<KeyState, int> KeysValues()
	{
		return new TheoryData<KeyState, int>
		{
			{ KeyState.None, 0 },
			{ KeyState.Left1, 1 },
			{ KeyState.Right1, 2 },
			{ KeyState.Left2, 4 },
			{ KeyState.Right2, 8 },
			{ KeyState.Smoke, 16 }
		};
	}

	public static TheoryData<TaikoDrumState, int> TaikoValues()
	{
		return new TheoryData<TaikoDrumState, int>
		{
			{ TaikoDrumState.None, 0 },
			{ TaikoDrumState.Don, 1 },
			{ TaikoDrumState.Kat, 2 },
			{ TaikoDrumState.Right, 4 },
			{ TaikoDrumState.Big, 8 },
			{ TaikoDrumState.LargeDouble, 16 }
		};
	}

	[Theory]
	[MemberData(nameof(KeysValues))]
	public void NamedButton_DocumentedValue(KeyState buttonState, int wireValue)
	{
		Assert.Equal(wireValue, (int)buttonState);
	}

	[Theory]
	[MemberData(nameof(TaikoValues))]
	public void NamedDrumZone_DocumentedValue(TaikoDrumState drumStateZone, int wireValue)
	{
		Assert.Equal(wireValue, (int)drumStateZone);
	}
}