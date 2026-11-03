using Basil.Application.Multiplayer.Objects;
using Basil.Bot.Commands.Mp;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     Every outcome the server can answer with has the reply that names it, rather than the generic fallback.
///     Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpRepliesTests
{
	[Fact]
	public void Of_Locked_NamesThatRoomsAreLocked()
	{
		Assert.Same(MpReplies.RoomsLocked, MpReplies.Of(RoomOutcome.Locked));
	}

	[Fact]
	public void Of_EveryOutcomeOtherThanOk_HasItsOwnReply()
	{
		foreach (var outcome in Enum.GetValues<RoomOutcome>().Where(outcome => outcome != RoomOutcome.Ok))
			Assert.NotSame(MpReplies.GenericFailed, MpReplies.Of(outcome));
	}
}
