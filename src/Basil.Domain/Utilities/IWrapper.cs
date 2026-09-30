namespace Basil.Domain.Utilities;

/// <summary>A model that pairs an identity with the data it identifies.</summary>
/// <typeparam name="T">The type of the wrapped data.</typeparam>
public interface IWrapper<out T>
{
	/// <summary>Gets the wrapped data.</summary>
	T Value { get; }
}
