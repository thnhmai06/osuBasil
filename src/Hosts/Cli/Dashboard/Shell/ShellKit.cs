using Basil.Host.Cli.Dashboard.Shell.Toasts;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The parts of the shell that every tab uses to talk to the operator.</summary>
/// <param name="Dialogs">Shows the modal forms.</param>
/// <param name="Toasts">Shows how something ended.</param>
/// <param name="Changes">Tells the pages when an action changed something.</param>
internal sealed record ShellKit(Dialogs Dialogs, ToastTray Toasts, Changes Changes);
