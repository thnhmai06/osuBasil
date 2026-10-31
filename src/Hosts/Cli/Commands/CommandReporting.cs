using System.Text;
using Basil.Application.Common.Common;
using Basil.Host.Shared;

namespace Basil.Host.Cli.Commands;

/// <summary>Turns the results of Application behaviours into console output and exit codes.</summary>
public static class CommandReporting
{
	/// <summary>Prints the confirmation of an action when it worked, or its refusal when it did not.</summary>
	/// <param name="call">The running command.</param>
	/// <param name="result">What the behaviour returned.</param>
	/// <param name="confirmation">The line to print on success.</param>
	/// <returns>The exit code.</returns>
	public static int Report<TOutcome>(this CommandCall call, Result<TOutcome> result, string confirmation)
		where TOutcome : struct, Enum
	{
		if (result.IsOk)
		{
			call.Output.Ok(confirmation);
			return ExitCodes.Success;
		}

		return call.Refuse(result);
	}

	/// <summary>Prints the refusal in a result that is not ok.</summary>
	public static int Refuse<TOutcome>(this CommandCall call, Result<TOutcome> result) where TOutcome : struct, Enum
	{
		// The console skips the rights checks, so a missing permission only happens through a bug; it is still shown.
		var info = result.Missing is null
			? OutcomeErrors.Describe(result.Outcome)
			: new ErrorInfo(OutcomeErrors.PermissionDeniedCode, ErrorKind.PermissionDenied);
		call.Output.Error(info.Code, Humanize(info.Code));
		return info.Kind == ErrorKind.Invalid ? ExitCodes.Invalid : ExitCodes.Refused;
	}

	/// <summary>Prints that something does not exist.</summary>
	public static int NotFound(this CommandCall call, string what)
	{
		call.Output.Error("notFound", $"No such {what}.");
		return ExitCodes.Refused;
	}

	/// <summary>Writes a camelCase code as a sentence: <c>roomClosed</c> is <c>Room closed.</c>.</summary>
	public static string Humanize(string code)
	{
		var text = new StringBuilder();
		foreach (var c in code)
			if (char.IsUpper(c) && text.Length > 0)
			{
				text.Append(' ');
				text.Append(char.ToLowerInvariant(c));
			}
			else
			{
				text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : c);
			}

		return text.Append('.').ToString();
	}
}