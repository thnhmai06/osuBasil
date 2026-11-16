using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Microsoft.Extensions.Time.Testing;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The detail frame: the order of its actions, what it never shows as text, and the results that arrive too late.</summary>
[Collection(HostedTerminalCollection.Name)]
public sealed class DetailPanelTests
{
	private static DetailPanel Create(Action<Action>? post = null)
	{
		var glyphs = new Glyphs(false);
		var palette = new Palette(false);
		post ??= action => action();
		var toasts = new ToastTray(glyphs, palette, new FakeTimeProvider(), post);
		return new DetailPanel("Users", glyphs, palette, new Dialogs(), toasts, post, new Changes());
	}

	private static List<Button> Buttons(DetailPanel panel)
	{
		return [.. panel.View.EnumerateVisualsDepthFirst().OfType<Button>()];
	}

	private static string Label(Button button)
	{
		return button.EnumerateVisualsDepthFirst().OfType<TextBlock>().First().Text ?? string.Empty;
	}

	private static DetailAction Act(string label, DetailActionKind kind)
	{
		return new DetailAction(label, () => { }, kind);
	}

	[Fact]
	public void Actions_StandNavigateFirstNormalInTheMiddleDestructiveLast_WhateverTheInputOrder_AndCloseEndsTheList()
	{
		var panel = Create();

		panel.Show(new DetailView("peppy", [],
		[
			Act("Close session", DetailActionKind.Destructive), Act("Kick", DetailActionKind.Normal),
			DetailAction.Open("Scores", () => new DetailView("Scores", [], []))
		]));

		Assert.Equal(["Scores…", "Kick", "Close session", "Close"], Buttons(panel).Select(Label));
	}

	[Fact]
	public void Actions_DestructiveButton_IsInTheErrorTone()
	{
		var panel = Create();

		panel.Show(new DetailView("peppy", [], [Act("Kick", DetailActionKind.Normal), Act("Close session", DetailActionKind.Destructive)]));

		var tones = Buttons(panel).ToDictionary(Label, button => button.Tone);
		Assert.Equal(ControlTone.Error, tones["Close session"]);
		Assert.NotEqual(ControlTone.Error, tones["Kick"]);
	}

	[Fact]
	public void NavigationAction_LabelEndsWithAnEllipsis()
	{
		Assert.EndsWith("…", DetailAction.Open("Sessions", () => new DetailView("Sessions", [], [])).Label);
		Assert.EndsWith("…", DetailAction.OpenAsync("Sessions", () => Task.FromResult(new DetailView("Sessions", [], []))).Label);
	}

	[Fact]
	public void ColouredField_IsShownWithoutMarkupTags()
	{
		var panel = Create();
		panel.Show(new DetailView("peppy", [("Name", "peppy", Ink.RoleStaff), ("Status", "Banned", Ink.Error)], []));

		var screen = HostedTerminal.Run(panel.View, []);

		Assert.Contains("peppy", screen);
		Assert.Contains("Banned", screen);
		Assert.DoesNotContain("[#", screen);
		Assert.DoesNotContain("[/]", screen);
		Assert.DoesNotContain("[cyan]", screen);
		Assert.DoesNotContain("[red]", screen);
	}

	private static string Everything(DetailPanel panel)
	{
		return string.Join("|", panel.View.EnumerateVisualsDepthFirst().OfType<Markup>().Select(markup => markup.Text));
	}

	// A post that keeps what is posted, so that a test decides when the result of a read reaches the interface.
	private sealed class Posted
	{
		private readonly TaskCompletionSource<Action> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<Action> First => _first.Task;

		public void Post(Action action)
		{
			// The interface thread's own posts (a click, a toast) run at once; only the first one from a reader is held.
			_first.TrySetResult(action);
		}
	}

	[Fact]
	public async Task AsyncResult_ThatArrivesAfterBack_IsDropped()
	{
		var posted = new Posted();
		var panel = Create(posted.Post);
		var read = new TaskCompletionSource<DetailView>(TaskCreationOptions.RunContinuationsAsynchronously);
		panel.ShowAsync("Old", () => read.Task);

		panel.Back();
		panel.Show(new DetailView("New", [], []));
		read.SetResult(new DetailView("Late", [("Field", "late value", Ink.Text)], []));
		(await posted.First.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))();

		var shown = Everything(panel);
		Assert.Contains("New", shown);
		Assert.DoesNotContain("Late", shown);
		Assert.DoesNotContain("late value", shown);
	}

	[Fact]
	public async Task AsyncResult_ThatArrivesAfterClose_DoesNotReopenTheFrame()
	{
		var posted = new Posted();
		var panel = Create(posted.Post);
		var read = new TaskCompletionSource<DetailView>(TaskCreationOptions.RunContinuationsAsynchronously);
		panel.ShowAsync("Old", () => read.Task);

		panel.Close();
		read.SetResult(new DetailView("Late", [], []));
		(await posted.First.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))();

		Assert.False(panel.IsOpen);
		Assert.DoesNotContain("Late", Everything(panel));
	}
}
