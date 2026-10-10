namespace Basil.Infrastructure.Storage.Common;

/// <summary>Data every client uses, held in memory from startup for the server's whole life.</summary>
// TODO: Chuyển nó thành abstract class, đưa logic triển khai chính vào đây. Derived chỉ việc triển khai logic quan hệ,
// không quan tâm tương tác như nào (abstract class này sẽ lo hết) - (liệu có thể?)
internal interface IResident // đổi thành tên khác.
{
	/// <summary>Loads the data into memory; called once at startup, after the database is up to date.</summary>
	Task LoadAsync(CancellationToken cancellationToken);
}