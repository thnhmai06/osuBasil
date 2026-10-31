using System.Runtime.CompilerServices;

namespace Basil.Domain.Common;

/// <summary>Rules that every address the server keeps follows.</summary>
public static class UriExtensions
{
	extension(Uri value)
	{
		/// <summary>Tells whether an address is absolute with the http or https scheme.</summary>
		public bool IsHttp()
		{
			return value.IsAbsoluteUri && (value.Scheme == Uri.UriSchemeHttp || value.Scheme == Uri.UriSchemeHttps);
		}

		/// <summary>Returns the address unchanged, or throws when it is not absolute with the http or https scheme.</summary>
		/// <exception cref="ArgumentNullException">The address is <see langword="null" />.</exception>
		/// <exception cref="ArgumentException">The address is not absolute with an http or https scheme.</exception>
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