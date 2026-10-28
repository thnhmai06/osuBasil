using Basil.Application.Common;

namespace Basil.Host.Rest.Http;

/// <summary>Turns the result of an Application behaviour into the HTTP answer of the API.</summary>
public static class OutcomeResults
{
	/// <summary>Maps a refusal to its status; the table of codes is shared with the other transports.</summary>
	/// <param name="kind">The classification of the refusal.</param>
	/// <returns>The HTTP status.</returns>
	public static int StatusOf(ErrorKind kind)
	{
		return kind switch
		{
			ErrorKind.PermissionDenied or ErrorKind.Forbidden or ErrorKind.WrongPassword => StatusCodes.Status403Forbidden,
			ErrorKind.NotFound => StatusCodes.Status404NotFound,
			ErrorKind.Conflict or ErrorKind.Locked or ErrorKind.SystemAccount => StatusCodes.Status409Conflict,
			ErrorKind.Gone => StatusCodes.Status410Gone,
			ErrorKind.Invalid => StatusCodes.Status422UnprocessableEntity,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
		};
	}

	/// <summary>Answers a refused result; the result must not be ok.</summary>
	public static IResult Refusal<TOutcome>(this Result<TOutcome> result) where TOutcome : struct, Enum
	{
		if (result.Missing is { } missing)
			return Api.Error(StatusCodes.Status403Forbidden, OutcomeErrors.PermissionDeniedCode,
				"Missing permissions.", missing);

		var info = OutcomeErrors.Describe(result.Outcome);
		return Api.Error(StatusOf(info.Kind), info.Code, info.Code);
	}

	/// <summary>Answers an operation without a payload: 200 when ok, otherwise the refusal.</summary>
	public static IResult Answer<TOutcome>(this Result<TOutcome> result) where TOutcome : struct, Enum
	{
		return result.IsOk ? Api.Done() : result.Refusal();
	}

	/// <summary>Answers an operation with a payload: 200 when ok, otherwise the refusal.</summary>
	public static IResult Answer<TOutcome, T>(this Result<TOutcome> result, Func<T> data)
		where TOutcome : struct, Enum
	{
		return result.IsOk ? Api.Ok(data()) : result.Refusal();
	}
}
