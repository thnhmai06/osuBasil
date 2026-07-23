using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Tests.Services.Multiplayer;

public class JsonMergePatchTests
{
    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private sealed record Sample(string Name, int Count, bool Flag);

    [Fact]
    public void Diff_NoChanges_ReturnsEmptyPatch()
    {
        var previous = new Sample("Alpha", 1, true);
        var current = new Sample("Alpha", 1, true);

        var patch = JsonMergePatch.Diff(previous, current, CamelCase);

        Assert.IsType<JsonObject>(patch);
        Assert.Empty((JsonObject)patch!);
    }

    [Fact]
    public void Diff_OneFieldChanged_PatchContainsOnlyThatField()
    {
        var previous = new Sample("Alpha", 1, true);
        var current = new Sample("Alpha", 2, true);

        var patch = JsonMergePatch.Diff(previous, current, CamelCase);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Single(obj);
        Assert.Equal(2, obj["count"]!.GetValue<int>());
    }

    [Fact]
    public void Diff_MultipleFieldsChanged_PatchContainsAll()
    {
        var previous = new Sample("Alpha", 1, true);
        var current = new Sample("Beta", 2, false);

        var patch = JsonMergePatch.Diff(previous, current, CamelCase);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Equal(3, obj.Count);
        Assert.Equal("Beta", obj["name"]!.GetValue<string>());
        Assert.Equal(2, obj["count"]!.GetValue<int>());
        Assert.False(obj["flag"]!.GetValue<bool>());
    }

    [Fact]
    public void Diff_MemberRemoved_RepresentedAsNull()
    {
        var previous = new JsonObject { ["a"] = 1, ["b"] = 2 };
        var current = new JsonObject { ["a"] = 1 };

        var patch = JsonMergePatch.Diff(previous, current);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Single(obj);
        Assert.True(obj.ContainsKey("b"));
        Assert.Null(obj["b"]);
    }

    [Fact]
    public void Diff_MemberAdded_IncludedInFull()
    {
        var previous = new JsonObject { ["a"] = 1 };
        var current = new JsonObject { ["a"] = 1, ["b"] = 2 };

        var patch = JsonMergePatch.Diff(previous, current);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Single(obj);
        Assert.Equal(2, obj["b"]!.GetValue<int>());
    }

    [Fact]
    public void Diff_NestedObjectChanged_OnlyNestedFieldIncluded()
    {
        var previous = new JsonObject { ["outer"] = new JsonObject { ["x"] = 1, ["y"] = 2 } };
        var current = new JsonObject { ["outer"] = new JsonObject { ["x"] = 1, ["y"] = 3 } };

        var patch = JsonMergePatch.Diff(previous, current);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Single(obj);
        var nested = Assert.IsType<JsonObject>(obj["outer"]);
        Assert.Single(nested);
        Assert.Equal(3, nested["y"]!.GetValue<int>());
    }

    [Fact]
    public void Diff_NestedObjectUnchanged_OmittedEntirely()
    {
        var previous = new JsonObject { ["outer"] = new JsonObject { ["x"] = 1 }, ["other"] = 1 };
        var current = new JsonObject { ["outer"] = new JsonObject { ["x"] = 1 }, ["other"] = 2 };

        var patch = JsonMergePatch.Diff(previous, current);

        var obj = Assert.IsType<JsonObject>(patch);
        Assert.Single(obj);
        Assert.False(obj.ContainsKey("outer"));
    }

    [Fact]
    public void Diff_ArrayChanged_ReplacedWholesaleNotMerged()
    {
        var previous = new JsonObject { ["items"] = new JsonArray(1, 2, 3) };
        var current = new JsonObject { ["items"] = new JsonArray(1, 2, 3, 4) };

        var patch = JsonMergePatch.Diff(previous, current);

        var obj = Assert.IsType<JsonObject>(patch);
        var array = Assert.IsType<JsonArray>(obj["items"]);
        Assert.Equal(4, array.Count);
    }
}
