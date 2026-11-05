namespace Basil.Application.Diagnostics.Objects;

/// <summary>What happened to a diagnostics operation that was not refused for lack of permissions.</summary>
public enum DiagnosticsOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1
}