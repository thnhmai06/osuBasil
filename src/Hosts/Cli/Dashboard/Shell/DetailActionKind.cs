namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>What an action of the detail frame does, which decides where its button stands.</summary>
internal enum DetailActionKind : byte
{
	/// <summary>Goes to another tab; its button comes first.</summary>
	Navigate,

	/// <summary>Changes something.</summary>
	Normal,

	/// <summary>Ends something; its button comes last, in the error colour.</summary>
	Destructive
}
