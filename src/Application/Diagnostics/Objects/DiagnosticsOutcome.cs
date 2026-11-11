namespace Basil.Application.Diagnostics.Objects;

/// <summary>What happened to a diagnostics operation that was not refused for lack of privileges.</summary>
public enum DiagnosticsOutcome : byte
{
	/// <summary>The caller lacks the privileges the operation needs.</summary>
	Denied = 0,

	/// <summary>The operation succeeded.</summary>
	Ok = 1
}