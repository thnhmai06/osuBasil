using Basil.Protocol.Bancho.Models.Replay;

namespace Basil.Protocol.Bancho.Tests.Models.Replay;

/// <summary>
///     Pins the wire bit values (documented in the osu! replay format) that the replay frame reader casts
///     bytes to: the mouse/keyboard <see cref="Keys" /> bitfield and the osu!taiko drum-zone
///     <see cref="TaikoByte" /> bitfield.
/// </summary>
public class ReplayButtonValuesTests
{
	public static TheoryData<Keys, int> KeysValues()
	{
		return new TheoryData<Keys, int>
		{
			{ Keys.None, 0 },
			{ Keys.Left1, 1 },
			{ Keys.Right1, 2 },
			{ Keys.Left2, 4 },
			{ Keys.Right2, 8 },
			{ Keys.Smoke, 16 }
		};
	}

	public static TheoryData<TaikoByte, int> TaikoValues()
	{
		return new TheoryData<TaikoByte, int>
		{
			{ TaikoByte.None, 0 },
			{ TaikoByte.Don, 1 },
			{ TaikoByte.Kat, 2 },
			{ TaikoByte.Right, 4 },
			{ TaikoByte.Big, 8 },
			{ TaikoByte.LargeDouble, 16 }
		};
	}

	[Theory]
	[MemberData(nameof(KeysValues))]
	public void Keys_NamedButton_HasTheDocumentedWireValue(Keys buttonState, int wireValue)
	{
		Assert.Equal(wireValue, (int)buttonState);
	}

	[Theory]
	[MemberData(nameof(TaikoValues))]
	public void TaikoByte_NamedDrumZone_HasTheDocumentedWireValue(TaikoByte drumZone, int wireValue)
	{
		Assert.Equal(wireValue, (int)drumZone);
	}
}