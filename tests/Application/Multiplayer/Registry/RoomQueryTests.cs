using Basil.Application.Multiplayer.Registry;
using Basil.Domain.Mechanics;

namespace Basil.Application.Multiplayer.Tests.Registry;

/// <summary>Reading the room search syntax.</summary>
public sealed class RoomQueryTests
{
	[Fact]
	public void EveryFilter_Read()
	{
		var query = RoomQuery.Parse("cup host:bob creator:2 player:\"a b\" status:Playing mode:1 password:no");

		Assert.Equal(new RoomQuery("cup", "bob", "2", "a b", RoomStatus.Playing, GameMode.Taiko, false), query);
	}

	[Theory]
	[InlineData("status:soon")]
	[InlineData("mode:9")]
	[InlineData("password:maybe")]
	[InlineData("size:4")]
	public void UnreadableFilter_StaysInText(string text)
	{
		Assert.Equal(new RoomQuery(text), RoomQuery.Parse(text));
	}

	[Fact]
	public void Empty_NoFilter()
	{
		Assert.Equal(new RoomQuery(), RoomQuery.Parse(string.Empty));
	}
}
