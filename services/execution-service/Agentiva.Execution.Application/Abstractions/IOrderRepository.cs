using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Domain.Orders;

namespace Agentiva.Execution.Application.Abstractions;

/// <summary>Access to the orders this service owns.</summary>
public interface IOrderRepository
{
    void Add(Order order);

    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken);

    Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an order on this symbol and side is currently open (created,
    /// submitted or partially filled). Backs the Trading Service's real
    /// duplicate-order check — see <c>HasOpenOrderQuery</c>.
    /// </summary>
    Task<bool> HasOpenOrderAsync(Symbol symbol, OrderSide side, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListRecentAsync(int limit, CancellationToken cancellationToken);
}
