using Basil.Host.Cli.Dashboard.Shell.Toasts;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed record ShellKit(Dialogs Dialogs, ToastTray Toasts, Changes Changes);