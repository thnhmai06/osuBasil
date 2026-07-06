using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Tests.Commands;

/// <summary>
/// Ported from app/commands.py's process_commands: trigger+alias lookup, privilege gating,
/// invoking the matched command. Reuses the same IEnumerable&lt;T&gt; DI + dispatch-by-key pattern
/// already used for IBanchoPacketHandler/BanchoPacketDispatcher, rather than the attribute+
/// reflection scan bancho.py uses — simpler and consistent with the rest of this codebase.
/// </summary>
public class CommandDispatcherTests
{
    private sealed class FakeCommand(string trigger, Privileges requiredPriv, bool hidden, Func<CommandContext, Task<string?>> onHandle) : ICommand
    {
        public string Trigger => trigger;
        public IReadOnlyList<string> Aliases { get; init; } = [];
        public Privileges RequiredPriv => requiredPriv;
        public bool Hidden => hidden;
        public string? Description => null;
        public Task<string?> HandleAsync(CommandContext ctx) => onHandle(ctx);
    }

    private static PlayerSession MakePlayer(Privileges priv = Privileges.Unrestricted) =>
        new(1, "cmyui", "token", priv, 0.0);

    [Fact]
    public async Task DispatchAsync_KnownTrigger_InvokesCommandAndReturnsResponse()
    {
        var command = new FakeCommand("roll", Privileges.Unrestricted, hidden: false, _ => Task.FromResult<string?>("you rolled a 42"));
        var dispatcher = new CommandDispatcher([command]);
        var player = MakePlayer();

        var result = await dispatcher.DispatchAsync(player, "roll", null, null);

        Assert.Equal("you rolled a 42", result?.Response);
        Assert.False(result?.Hidden);
    }

    [Fact]
    public async Task DispatchAsync_UnknownTrigger_ReturnsNull()
    {
        var dispatcher = new CommandDispatcher([]);
        var player = MakePlayer();

        var result = await dispatcher.DispatchAsync(player, "nonexistent", null, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task DispatchAsync_InsufficientPrivilege_DoesNotInvokeAndReturnsNull()
    {
        var invoked = false;
        var command = new FakeCommand("silence", Privileges.Moderator, hidden: true, _ => { invoked = true; return Task.FromResult<string?>("silenced"); });
        var dispatcher = new CommandDispatcher([command]);
        var player = MakePlayer(Privileges.Unrestricted); // not a moderator

        var result = await dispatcher.DispatchAsync(player, "silence x", null, null);

        Assert.Null(result);
        Assert.False(invoked);
    }

    [Fact]
    public async Task DispatchAsync_ByAlias_InvokesCommand()
    {
        var command = new FakeCommand("help", Privileges.Unrestricted, hidden: false, _ => Task.FromResult<string?>("help text"))
        {
            Aliases = ["h"],
        };
        var dispatcher = new CommandDispatcher([command]);
        var player = MakePlayer();

        var result = await dispatcher.DispatchAsync(player, "h", null, null);

        Assert.Equal("help text", result?.Response);
    }

    [Fact]
    public async Task DispatchAsync_PassesArgsAndRecipientContextToCommand()
    {
        CommandContext? captured = null;
        var command = new FakeCommand("echo", Privileges.Unrestricted, hidden: false, ctx => { captured = ctx; return Task.FromResult<string?>(null); });
        var dispatcher = new CommandDispatcher([command]);
        var player = MakePlayer();
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);

        await dispatcher.DispatchAsync(player, "echo one two", channel, null);

        Assert.NotNull(captured);
        Assert.Equal(["one", "two"], captured!.Args);
        Assert.Same(player, captured.Player);
        Assert.Same(channel, captured.Channel);
        Assert.Null(captured.PmTarget);
    }

    [Fact]
    public async Task DispatchAsync_HiddenCommand_ResultReflectsHidden()
    {
        var command = new FakeCommand("silence", Privileges.Unrestricted, hidden: true, _ => Task.FromResult<string?>("done"));
        var dispatcher = new CommandDispatcher([command]);
        var player = MakePlayer();

        var result = await dispatcher.DispatchAsync(player, "silence x", null, null);

        Assert.True(result?.Hidden);
    }

    [Fact]
    public async Task DispatchAsync_EmptyCommandText_ReturnsNull()
    {
        var dispatcher = new CommandDispatcher([]);
        var player = MakePlayer();

        var result = await dispatcher.DispatchAsync(player, "", null, null);

        Assert.Null(result);
    }
}
