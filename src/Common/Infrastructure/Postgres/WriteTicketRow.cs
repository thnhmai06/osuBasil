namespace Basil.Common.Infrastructure.Postgres;

/// <summary>
///     A stored row of the <c>write_tickets</c> table: the mark a write leaves in its own transaction, so that when a
///     connection drops while the transaction commits, the row tells whether it did.
/// </summary>
public sealed class WriteTicketRow
{
	/// <summary>Gets or sets the identifier of the committed write.</summary>
	public Guid Id { get; set; }

	/// <summary>Gets or sets when the write committed.</summary>
	public DateTimeOffset CreatedAt { get; set; }
}