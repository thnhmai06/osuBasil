using System.Globalization;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Common;

/// <summary>
///     The result of a behaviour that can be refused: what happened, or which permissions the caller lacks.
/// </summary>
/// <remarks>
///     Each object owns one outcome enum whose <c>Ok</c> member is <c>1</c> and whose <c>0</c> means "not set", so a
///     refusal (whose outcome is the default value) can never read as success. Callers test <see cref="IsOk" />.
/// </remarks>
/// <typeparam name="TOutcome">The outcome enum of the object that returns the result.</typeparam>
/// <param name="Outcome">What happened; the default value when the caller lacks permissions.</param>
/// <param name="Missing">The permissions the caller lacks, or <see langword="null" /> when none are missing.</param>
public readonly record struct Result<TOutcome>(TOutcome Outcome, UserPermissions? Missing = null)
	where TOutcome : struct, Enum
{
	private const int OkValue = 1;

	/// <summary>Gets a value that indicates whether the behaviour succeeded.</summary>
	public bool IsOk => Missing is null && Convert.ToInt32(Outcome, CultureInfo.InvariantCulture) == OkValue;

	/// <summary>Creates the result of a refusal for lack of permissions.</summary>
	/// <param name="missing">The permissions the caller lacks.</param>
	/// <returns>A result that is not ok and names <paramref name="missing" />.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="missing" /> is empty.</exception>
	public static Result<TOutcome> Denied(UserPermissions missing)
	{
		ArgumentOutOfRangeException.ThrowIfEqual(missing, UserPermissions.None);
		return new Result<TOutcome>(default, missing);
	}

	/// <summary>Wraps an outcome that is not a permission refusal.</summary>
	/// <param name="outcome">The outcome.</param>
	public static implicit operator Result<TOutcome>(TOutcome outcome)
	{
		return new Result<TOutcome>(outcome);
	}
}
