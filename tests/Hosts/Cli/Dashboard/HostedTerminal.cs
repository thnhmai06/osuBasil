using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>Every test that opens the process-wide terminal or builds a terminal UI tree runs in this collection, one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostedTerminalCollection
{
	/// <summary>The name of the collection.</summary>
	public const string Name = "HostedTerminal";
}

/// <summary>Runs a visual on a virtual terminal for a few frames, with the mouse clicks the test asks for.</summary>
internal static class HostedTerminal
{
	/// <summary>Runs the visual and returns what it wrote to the terminal.</summary>
	/// <param name="root">What to show.</param>
	/// <param name="clicks">The cells to click with the left button, one after the other.</param>
	/// <param name="width">The columns.</param>
	/// <param name="height">The rows.</param>
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
