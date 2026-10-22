using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Sessions;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Replies;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Bot.Tests.Support;

/// <summary>What the bot said to one command, split by where it said it.</summary>
public sealed class Said
{
	/// <summary>Gets the texts said where the command was typed (the channel, or the private chat with the bot).</summary>
	public List<string> Here { get; } = [];

	/// <summary>Gets the texts said in the typist's private messages.</summary>
	public List<string> Privately { get; } = [];

	/// <summary>Gets everything said, here first.</summary>
	public IEnumerable<string> All => Here.Concat(Privately);
}

/// <summary>Gives the commands of the bot a real application to run against and records what they say.</summary>
internal sealed class BotHarness : IDisposable
{
	/// <summary>Creates the harness.</summary>
	/// <param name="random">The dice the <c>!roll</c> command uses, or a fixed one when none is given.</param>
	public BotHarness(Random? random = null)
	{
		App = new TestApplication();
		Replies = new ReplyCatalog(ReplyDefinitions.In(typeof(Reply).Assembly),
			Options.Create(new RepliesOptions { Path = Path.Combine(Path.GetTempPath(), "basil-bot-tests-none.json") }),
			NullLogger<ReplyCatalog>.Instance);
		Users = App.Provider.GetRequiredService<IUserRepository>();
		Beatmaps = App.Provider.GetRequiredService<IBeatmapRepository>();
		Faqs = App.Provider.GetRequiredService<IFaqStorage>();
		Targets = new MpTargets();
		Mp = new MpCommands(App.Lobby, App.Users, Users, Beatmaps, Targets);
		Chat = new ChatCommands(Users, Faqs, Mp, random ?? new FixedRandom(0));
	}

	/// <summary>Gets the application the commands run against.</summary>
	public TestApplication App { get; }

	/// <summary>Gets the replies with the default wording, the same the commands say.</summary>
	public ReplyCatalog Replies { get; }

	/// <summary>Gets the user repository of the application.</summary>
	public IUserRepository Users { get; }

	/// <summary>Gets the beatmap repository of the application; a substitute that knows no beatmap.</summary>
	public IBeatmapRepository Beatmaps { get; }

	/// <summary>Gets the FAQ storage of the application; a substitute that knows no entry.</summary>
	public IFaqStorage Faqs { get; }

	/// <summary>Gets the memory of which room each user targets with <c>!mp</c>.</summary>
	public MpTargets Targets { get; }

	/// <summary>Gets the <c>!mp</c> commands.</summary>
	public MpCommands Mp { get; }

	/// <summary>Gets the chat commands.</summary>
	public ChatCommands Chat { get; }

	/// <inheritdoc />
	public void Dispose()
	{
		App.Dispose();
	}

	/// <summary>Types a message as a user and collects what the bot says.</summary>
	/// <param name="by">The connection of the typist.</param>
	/// <param name="channel">The channel it is typed in, or <see langword="null" /> for a private message to the bot.</param>
	/// <param name="text">The message.</param>
	/// <returns>Whether a command ran and succeeded, and what was said.</returns>
	public async Task<(bool Succeeded, Said Said)> TypeAsync(Connection by, string? channel, string text)
	{
		var said = new Said();
		var context = new CommandContext(by, channel, Replies,
			(line, _) =>
			{
				said.Here.Add(line);
				return Task.CompletedTask;
			},
			(line, _) =>
			{
				said.Privately.Add(line);
				return Task.CompletedTask;
			});
		var succeeded = await Chat.ExecuteAsync(context, text, TestContext.Current.CancellationToken);
		return (succeeded, said);
	}

	/// <summary>Gets the text of a reply with its arguments filled in, as the bot says it.</summary>
	/// <param name="reply">The reply.</param>
	/// <param name="arguments">The values of its arguments, or <see langword="null" /> when it has none.</param>
	/// <returns>The text.</returns>
	public string Text(Reply reply, object? arguments = null)
	{
		return Replies.Text(reply, arguments);
	}

	/// <summary>Dice that always give the same number.</summary>
	private sealed class FixedRandom(long value) : Random
	{
		public override long NextInt64(long minValue, long maxValue)
		{
			return Math.Clamp(value, minValue, maxValue - 1);
		}
	}
}
