using Microsoft.Extensions.Logging;
using Basil.Application.Users;
using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Users.Messages;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class CommandActions(
	IServiceProvider services,
	ConsoleTab console,
	ToastTray toasts,
	Changes changes,
	Action<Action> post,
	ILogger logger)
{
	public static string Quote(string value)
	{
		return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
	}

	public void Run(string line)
	{
		var output = new ConcurrentQueue<OutputLine>();
		_ = console.RunCommand(line, output.Enqueue)
			.ContinueWith(_ => post(() => Finish(output)), TaskScheduler.Default);
	}

	public void Notify(string text)
	{
		toasts.Show(text, ToastKind.Warning);
	}

	public void When(Func<Task<bool>> check, string otherwise, Action then)
	{
		_ = check().ContinueWith(task => post(() =>
		{
			if (task.IsFaulted) logger.LogWarning(task.Exception, "A check before an action failed");
			if (task is { IsCompletedSuccessfully: true, Result: true }) then();
			else toasts.Show(otherwise, ToastKind.Warning);
		}), TaskScheduler.Default);
	}

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
			try
			{
				var record = text.Length == 0
					? null
					: await services.GetRequiredService<ISender>().Send(new FindUserByName(text));
				post(() =>
				{
					if (record is null) toasts.Show($"No such user: {text}.", ToastKind.Error);
					else Run(string.Format(CultureInfo.InvariantCulture, template, record.Id));
				});
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "The user {Name} could not be looked up", text);
				post(() => toasts.Show($"The user {text} could not be looked up.", ToastKind.Error));
			}
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