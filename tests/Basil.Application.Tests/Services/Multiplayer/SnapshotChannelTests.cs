using System.Text;
using System.Text.Json;
using Basil.Application.Services;

namespace Basil.Application.Tests.Services.Multiplayer;

public class SnapshotChannelTests
{
	[Fact]
	public void Latest_BeforeAnyPublish_IsNull()
	{
		var channel = new SnapshotChannel<Sample>();

		Assert.Null(channel.Latest);
	}

	[Fact]
	public void Publish_FirstCall_LatestReflectsFullState()
	{
		var channel = new SnapshotChannel<Sample>();
		var state = new Sample("Alpha", 1);

		channel.Publish(state);

		Assert.Same(state, channel.Latest);
	}

	[Fact]
	public void Publish_SecondCallWithChange_ReturnsDeltaContainingOnlyChangedField()
	{
		var channel = new SnapshotChannel<Sample>();
		channel.Publish(new Sample("Alpha", 1));

		var patchBytes = channel.Publish(new Sample("Alpha", 2));

		Assert.NotNull(patchBytes);
		var json = JsonDocument.Parse(Encoding.UTF8.GetString(patchBytes));
		var obj = json.RootElement;
		Assert.Single(obj.EnumerateObject());
		Assert.Equal(2, obj.GetProperty("count").GetInt32());
	}

	[Fact]
	public void Publish_UpdatesLatestToNewestState()
	{
		var channel = new SnapshotChannel<Sample>();
		channel.Publish(new Sample("Alpha", 1));
		var second = new Sample("Beta", 2);

		channel.Publish(second);

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
		var channel = new SnapshotChannel<Sample>();
		channel.Publish(new Sample("Alpha", 1));

		var patchBytes = channel.Publish(new Sample("Alpha", 1));

		Assert.Null(patchBytes);
	}

	private sealed record Sample(string Name, int Count);
}