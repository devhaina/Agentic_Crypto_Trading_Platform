using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Trading.Application.Abstractions;

namespace Agentiva.Trading.Application.Intents;

/// <summary>Fetches one trading intent.</summary>
/// <param name="TradingIntentId">Intent identity.</param>
public sealed record GetTradingIntentQuery(Guid TradingIntentId) : IQuery<Result<TradingIntentResponse>>;

/// <summary>Lists recent trading intents, newest first.</summary>
/// <param name="Limit">Maximum rows to return.</param>
public sealed record ListTradingIntentsQuery(int Limit = 50)
    : IQuery<Result<IReadOnlyList<TradingIntentResponse>>>;

/// <summary>Handles <see cref="GetTradingIntentQuery"/>.</summary>
public sealed class GetTradingIntentQueryHandler(ITradingIntentRepository intents)
    : IRequestHandler<GetTradingIntentQuery, Result<TradingIntentResponse>>
{
    public async Task<Result<TradingIntentResponse>> HandleAsync(
        GetTradingIntentQuery request,
        CancellationToken cancellationToken)
    {
        var intent = await intents.GetByIdAsync(
            TradingIntentId.From(request.TradingIntentId), cancellationToken);

        return intent is null
            ? Result.Failure<TradingIntentResponse>(Error.NotFound(
                "trading.intent.not_found",
                $"No trading intent exists with id {request.TradingIntentId}."))
            : Result.Success(CreateTradingIntentCommandHandler.ToResponse(intent));
    }
}

/// <summary>Handles <see cref="ListTradingIntentsQuery"/>.</summary>
public sealed class ListTradingIntentsQueryHandler(ITradingIntentRepository intents)
    : IRequestHandler<ListTradingIntentsQuery, Result<IReadOnlyList<TradingIntentResponse>>>
{
    public async Task<Result<IReadOnlyList<TradingIntentResponse>>> HandleAsync(
        ListTradingIntentsQuery request,
        CancellationToken cancellationToken)
    {
        var recent = await intents.ListRecentAsync(request.Limit, cancellationToken);

        IReadOnlyList<TradingIntentResponse> responses = recent
            .Select(CreateTradingIntentCommandHandler.ToResponse)
            .ToArray();

        return Result.Success(responses);
    }
}
