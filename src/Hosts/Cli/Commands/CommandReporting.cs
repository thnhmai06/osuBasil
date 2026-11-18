using System.Text;
using Basil.Host.Shared.Errors;

namespace Basil.Host.Cli.Commands;

public static class CommandReporting
{
	public static int Report<TOutcome>(this CommandCall call, TOutcome result, string confirmation,
		string? missing = null)
		where TOutcome : struct, Enum
	{
		if (OutcomeErrors.IsOk(result))
		{
			call.Output.Ok(confirmation);
			return ExitCodes.Success;
		}

		return missing is not null && OutcomeErrors.Describe(result).Kind == ErrorKind.NotFound
			? call.NotFound(missing)
			: call.Refuse(result);
	}

	public static int Refuse<TOutcome>(this CommandCall call, TOutcome result) where TOutcome : struct, Enum
	{
		var info = OutcomeErrors.Describe(result);
		call.Output.Error(info.Code, Humanize(info.Code));
		return info.Kind == ErrorKind.Invalid ? ExitCodes.Invalid : ExitCodes.Refused;
	}

	public static int NotFound(this CommandCall call, string what)
	{
		call.Output.Error("notFound", $"No such {what}.");
		return ExitCodes.Refused;
	}

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