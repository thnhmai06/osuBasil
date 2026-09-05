using System.Text;
using System.Text.Json;
using Basil.Application.Services;

namespace Basil.Application.Tests.Services.Multiplayer;

public class SnapshotChannelTests
{
	[Fact]
	public void Latest_BeforeAnyPublish_IsNull()
	{
		var channel = new SnapshotChannel<Sample>("test");

		Assert.Null(channel.Latest);
	}

	[Fact]
	public void Publish_FirstCall_LatestReflectsFullState()
	{
		var channel = new SnapshotChannel<Sample>("test");
		var state = new Sample("Alpha", 1);

		channel.Publish(state, 1);

		Assert.Same(state, channel.Latest);
	}

	[Fact]
	public void Publish_SecondCallWithChange_ReturnsDeltaContainingOnlyChangedField()
	{
		var channel = new SnapshotChannel<Sample>("test");
		channel.Publish(new Sample("Alpha", 1), 1);

		var patchBytes = channel.Publish(new Sample("Alpha", 2), 2);

		Assert.NotNull(patchBytes);
		var json = JsonDocument.Parse(Encoding.UTF8.GetString(patchBytes));
		var obj = json.RootElement;
		Assert.Single(obj.EnumerateObject());
		Assert.Equal(2, obj.GetProperty("count").GetInt32());
	}

	[Fact]
	public void Publish_UpdatesLatestToNewestState()
	{
		var channel = new SnapshotChannel<Sample>("test");
		channel.Publish(new Sample("Alpha", 1), 1);
		var second = new Sample("Beta", 2);

		channel.Publish(second, 2);

		Assert.Same(second, channel.Latest);
	}

	/// <summary>
	///     Regression test (ADR-004 "{}" spam fix): Publish used to return a literal "{}" for a
	///     no-op update, which SnapshotChannel.Publish's caller broadcast on every call regardless of
	///     whether anything changed. It now returns null so a caller can skip publishing entirely.
	/// </summary>
	[Fact]
	public void Publish_NoActualChange_ReturnsNull()
	{
		var channel = new SnapshotChannel<Sample>("test");
		channel.Publish(new Sample("Alpha", 1), 1);

		var patchBytes = channel.Publish(new Sample("Alpha", 1), 2);

		Assert.Null(patchBytes);
	}

	/// <summary>
	///     Regression test (ADR-004 4b): the exact race the sequence gate exists to resolve.
	///     Once a newer sequence has been applied (as would happen when its unlocked
	///     build-and-publish finished first), a call carrying an older sequence -- from a mutation
	///     that actually happened earlier but whose unlocked build finished later -- must be dropped
	///     rather than reverting Latest to stale content.
	/// </summary>
	[Fact]
	public void Publish_OlderSequenceArrivesAfterNewer_DroppedAndLatestUnchanged()
	{
		var channel = new SnapshotChannel<Sample>("test");
		var newer = new Sample("Newer", 5);
		channel.Publish(newer, 5);

		var patchBytes = channel.Publish(new Sample("Older", 4), 4);

		Assert.Null(patchBytes);
		Assert.Same(newer, channel.Latest);
	}

	private sealed record Sample(string Name, int Count);
}