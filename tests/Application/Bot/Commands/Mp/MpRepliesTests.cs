using Basil.Application.Bot.Commands.Mp;
using Basil.Application.Multiplayer.Objects;

namespace Basil.Application.Bot.Tests.Commands.Mp;

public sealed class MpRepliesTests
{
	[Fact]
	public void Locked_NamesLocked()
	{
		Assert.Same(MpReplies.RoomsLocked, MpReplies.Of(RoomOutcome.Locked));
	}

	[Fact]
	public void EveryOutcome_OwnReply()
	{
		foreach (var outcome in Enum.GetValues<RoomOutcome>().Where(outcome => outcome != RoomOutcome.Ok))
			Assert.NotSame(MpReplies.GenericFailed, MpReplies.Of(outcome));
	}
}