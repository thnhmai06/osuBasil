using System.Runtime.CompilerServices;

namespace Basil.Shared.Utilities;

public static class UriExtensions
{
	extension(Uri value)
	{
		public bool IsHttp()
		{
			return value.IsAbsoluteUri && (value.Scheme == Uri.UriSchemeHttp || value.Scheme == Uri.UriSchemeHttps);
		}

		public Uri ThrowIfHttp([CallerArgumentExpression(nameof(value))] string? paramName = null)
		{
			if (value is null)
				throw new ArgumentNullException(paramName);

			return value.IsHttp()
				? value
				: throw new ArgumentException("The address must be absolute with an http or https scheme.", paramName);
		}
	}
}