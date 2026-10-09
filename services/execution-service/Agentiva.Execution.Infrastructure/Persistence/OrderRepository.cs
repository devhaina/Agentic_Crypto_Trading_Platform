using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Application.Abstractions;
using Agentiva.Execution.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Execution.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IOrderRepository"/>.</summary>
public sealed class OrderRepository(ExecutionDbContext context) : IOrderRepository
{
    private static readonly OrderStatus[] OpenStatuses =
        [OrderStatus.Created, OrderStatus.Submitted, OrderStatus.PartiallyFilled];

    public void Add(Order order) => context.Orders.Add(order);

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken)
        => context.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken)
        => context.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);

    public Task<bool> HasOpenOrderAsync(Symbol symbol, OrderSide side, CancellationToken cancellationToken)
        => context.Orders.AnyAsync(
            o => o.Symbol == symbol && o.Side == side && OpenStatuses.Contains(o.Status),
            cancellationToken);

    public async Task<IReadOnlyList<Order>> ListRecentAsync(int limit, CancellationToken cancellationToken)
        => await context.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
}
