using System.Text;
using System.Text.Json;
using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Tests.Services.Multiplayer;

public class SnapshotChannelTests
{
    private sealed record Sample(string Name, int Count);

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

        var json = JsonDocument.Parse(Encoding.UTF8.GetString(patchBytes));
        var obj = json.RootElement;
        Assert.Equal(1, obj.EnumerateObject().Count());
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

    [Fact]
    public void Publish_NoActualChange_ReturnsEmptyPatch()
    {
        var channel = new SnapshotChannel<Sample>();
        channel.Publish(new Sample("Alpha", 1));

        var patchBytes = channel.Publish(new Sample("Alpha", 1));

        var json = JsonDocument.Parse(Encoding.UTF8.GetString(patchBytes));
        Assert.Empty(json.RootElement.EnumerateObject());
    }
}
