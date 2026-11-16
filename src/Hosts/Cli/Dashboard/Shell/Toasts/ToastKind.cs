namespace Basil.Host.Cli.Dashboard.Shell.Toasts;

/// <summary>How the thing a toast reports ended.</summary>
internal enum ToastKind : byte
{
	/// <summary>It went well.</summary>
	Info,

	/// <summary>It needs attention.</summary>
	Warning,

	/// <summary>It failed; the toast stays longer.</summary>
	Error
}
