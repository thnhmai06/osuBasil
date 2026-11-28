namespace Basil.Host.Rest.Http;

public static class OutcomeResults
{
	public static int StatusOf(ErrorKind kind)
	{
		return kind switch
		{
			ErrorKind.PrivilegeDenied or ErrorKind.Forbidden or ErrorKind.WrongPassword => StatusCodes
				.Status403Forbidden,
			ErrorKind.NotFound => StatusCodes.Status404NotFound,
			ErrorKind.Conflict or ErrorKind.Locked or ErrorKind.SystemAccount => StatusCodes.Status409Conflict,
			ErrorKind.Gone => StatusCodes.Status410Gone,
			ErrorKind.Invalid => StatusCodes.Status422UnprocessableEntity,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
		};
	}

	extension(Enum result)
	{
		public IResult Refusal()
		{
			var info = OutcomeErrors.Describe(result);
			return Api.Error(StatusOf(info.Kind), info.Code, OutcomeTexts.Of(result, info));
		}

		public IResult Answer()
		{
			return OutcomeErrors.IsOk(result) ? Api.Done() : result.Refusal();
		}

		public IResult Answer<T>(Func<T> data)
		{
			return OutcomeErrors.IsOk(result) ? Api.Ok(data()) : result.Refusal();
		}
	}
}