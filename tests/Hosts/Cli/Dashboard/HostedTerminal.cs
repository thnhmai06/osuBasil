using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Tests.Dashboard;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostedTerminalCollection
{
	public const string Name = "HostedTerminal";
}

internal static class HostedTerminal
{
	public static string Run(Visual root, IReadOnlyList<(int X, int Y)> clicks, int width = 80, int height = 24)
	{
		var output = new StringWriter();
		var backend = new VirtualTerminalBackend(output, new StringWriter(), new TerminalSize(width, height),
			new TerminalCapabilities { AnsiEnabled = true, ColorLevel = TerminalColorLevel.TrueColor, IsInputRedirected = false, IsOutputRedirected = false, SupportsMouse = true, SupportsAlternateScreen = true, SupportsRawMode = true }, false);
		Terminal.Open(backend, new TerminalOptions(), false);
		try
		{
			var frame = 0;
			Terminal.Instance.Run(root, _ =>
			{
				frame++;
				var step = frame - 3;
				if (step >= 0 && step / 2 < clicks.Count)
				{
					var (x, y) = clicks[step / 2];
					backend.PushEvent(new TerminalMouseEvent
					{
						X = x, Y = y, Button = TerminalMouseButton.Left,
						Kind = step % 2 == 0 ? TerminalMouseKind.Down : TerminalMouseKind.Up
					});
				}

				return frame >= 8 + clicks.Count * 2 ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
			}, new TerminalRunOptions
			{
				ExitGesture = null, LoopMode = TerminalLoopMode.Polling, UpdateWaitDuration = TimeSpan.FromMilliseconds(1)
			});
		}
		finally
		{
			Terminal.Close();
		}

		return output.ToString();
	}
}
