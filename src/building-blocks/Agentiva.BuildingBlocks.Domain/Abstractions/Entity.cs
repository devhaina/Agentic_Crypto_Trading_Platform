namespace Agentiva.BuildingBlocks.Domain.Abstractions;

/// <summary>Base class for entities identified by a strongly typed identifier.</summary>
/// <typeparam name="TId">The entity's identifier type.</typeparam>
public abstract class Entity<TId>
    where TId : struct
{
    protected Entity(TId id) => Id = id;

    /// <summary>Parameterless constructor for EF Core materialisation.</summary>
    protected Entity()
    {
    }

    public TId Id { get; protected set; }

    public override bool Equals(object? obj)
        => obj is Entity<TId> other && other.GetType() == GetType() && Id.Equals(other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// An aggregate root: the only entity another aggregate may hold a reference to,
/// and the unit of transactional consistency.
/// </summary>
/// <typeparam name="TId">The aggregate's identifier type.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Events raised since the aggregate was loaded, drained by the unit of work
    /// after a successful save.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Optimistic concurrency token, mapped to a PostgreSQL <c>xmin</c> system column.
    /// </summary>
    /// <remarks>
    /// Two concurrent writers to the same position or order must not silently
    /// interleave — the second commit fails and retries against fresh state.
    /// Without this, a fill applied twice from two workers would double-count a
    /// position.
    /// </remarks>
    public uint Version { get; protected set; }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Removes and returns pending events. Called only by the unit of work.</summary>
    public IReadOnlyCollection<IDomainEvent> DrainDomainEvents()
    {
        var drained = _domainEvents.ToArray();
        _domainEvents.Clear();
        return drained;
    }
}
