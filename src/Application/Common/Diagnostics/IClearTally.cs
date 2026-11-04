namespace Basil.Application.Common.Diagnostics;

/// <summary>A running count that an operator can reset to zero, such as the exceptions the process has thrown.</summary>
public interface IClearTally
{
	/// <summary>Forgets the counted events and starts counting again from zero.</summary>
	void Reset();
}
