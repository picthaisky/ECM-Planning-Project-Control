namespace CMPlus.Domain.Common;

/// <summary>
/// Base type for every Domain entity. Generates a time-ordered UUIDv7 id
/// (<see cref="Guid.CreateVersion7()"/>) rather than the default random v4 <c>Guid.NewGuid()</c>,
/// per docs/db-conventions.md §3.2: the id is also the SQL Server clustered index key, so
/// time-ordered ids keep inserts append-mostly-sequential instead of causing page-split
/// fragmentation as tables such as <c>ActivityProgressLog</c> grow to hundreds of thousands of
/// rows. Ids are always assigned in the Domain layer, never by the database.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }
}
