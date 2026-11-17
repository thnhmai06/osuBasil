using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     Runs the console commands that the buttons of the detail frame stand for, tells the operator how each one really
///     ended and makes every page read again what it may have changed.
/// </summary>
internal sealed class CommandActions(IServiceProvider services, ConsoleTab console, ToastTray toasts, Changes changes,
	Action<Action> post)
{
	/// <summary>Writes a value as one word of a command line.</summary>
	/// <param name="value">The value, such as a name or a reason.</param>
	public static string Quote(string value)
	{
		return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
	}

	/// <summary>Runs a command line; a toast tells how it ended.</summary>
	/// <param name="line">The command line, which is also shown in the console log.</param>
	public void Run(string line)
	{
		var output = new ConcurrentQueue<OutputLine>();
		_ = console.RunCommand(line, output.Enqueue)
			.ContinueWith(_ => post(() => Finish(output)), TaskScheduler.Default);
	}

	/// <summary>Tells the operator that nothing was run, such as when a form was left empty.</summary>
	/// <param name="text">What to say.</param>
	public void Notify(string text)
	{
		toasts.Show(text, ToastKind.Warning);
	}

	/// <summary>Runs <paramref name="then" /> when a check holds, and otherwise tells the operator why not.</summary>
	/// <param name="check">Reads whether the action can go on.</param>
	/// <param name="otherwise">What to say when it cannot.</param>
	/// <param name="then">The action.</param>
	public void When(Func<Task<bool>> check, string otherwise, Action then)
	{
		_ = check().ContinueWith(task => post(() =>
		{
			if (task.IsCompletedSuccessfully && task.Result) then();
			else toasts.Show(otherwise, ToastKind.Warning);
		}), TaskScheduler.Default);
	}

	/// <summary>Runs a command line that names an account the operator typed, by id or by name.</summary>
	/// <param name="template">The command line, with <c>{0}</c> where the account id goes.</param>
	/// <param name="who">The id or the name of the account.</param>
	public void RunForUser(string template, string who)
	{
		var text = who.Trim();
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
		{
			Run(string.Format(CultureInfo.InvariantCulture, template, id));
			return;
		}

		_ = Task.Run(async () =>
		{
			var record = text.Length == 0
				? null
				: await services.GetRequiredService<ISender>().Send(new GetUser(UserReference.ByName(text)));
			post(() =>
			{
				if (record is null) toasts.Show($"No such user: {text}.", ToastKind.Error);
				else Run(string.Format(CultureInfo.InvariantCulture, template, record.Id));
			});
		});
	}

	// The last refusal wins over any confirmation; a command that said nothing is not reported as a success.
	private void Finish(IEnumerable<OutputLine> output)
	{
		var lines = output.ToList();
		if (lines.LastOrDefault(line => line.Kind == OutputKind.Error).Text is { Length: > 0 } error)
			toasts.Show(Message(error), ToastKind.Error);
		else if (lines.LastOrDefault(line => line.Kind == OutputKind.Ok).Text is { Length: > 0 } ok)
			toasts.Show(First(ok));
		else
			toasts.Show("The command gave no answer.", ToastKind.Warning);

		changes.Raise();
	}

	// "error: notFound: No such session." is shown as "No such session."
	private static string Message(string text)
	{
		var line = First(text);
		var parts = line.Split(": ", 3);
		return parts is ["error", _, _] ? parts[2] : line;
	}

	private static string First(string text)
	{
		return text.Split('\n')[0].TrimEnd('\r');
	}
}
