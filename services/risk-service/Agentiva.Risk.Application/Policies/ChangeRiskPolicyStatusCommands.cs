using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Risk.Application.Abstractions;
using FluentValidation;

namespace Agentiva.Risk.Application.Policies;

/// <summary>Deactivates a policy so it can no longer be applied to an evaluation.</summary>
public sealed record DeactivateRiskPolicyCommand(Guid RiskPolicyId, string UpdatedBy)
    : ICommand<Result<RiskPolicyDto>>;

/// <summary>Promotes a policy to be the one applied when none is specified.</summary>
/// <remarks>
/// Does not demote the previous default: <see cref="IRiskPolicyRepository.GetDefaultAsync"/>
/// filters on <c>IsDefault</c>, so if a caller marks two policies as default
/// without demoting the first, the query's own tie-break
/// (<c>FirstOrDefaultAsync</c>) rather than an explicit, visible rule would
/// decide which one actually governs trading. The handler enforces exactly
/// one default exists by demoting every other one first.
/// </remarks>
public sealed record MarkRiskPolicyAsDefaultCommand(Guid RiskPolicyId, string UpdatedBy)
    : ICommand<Result<RiskPolicyDto>>;

/// <summary>Validates the actor is identified on both status-changing commands.</summary>
public sealed class DeactivateRiskPolicyCommandValidator : AbstractValidator<DeactivateRiskPolicyCommand>
{
    public DeactivateRiskPolicyCommandValidator()
    {
        RuleFor(c => c.RiskPolicyId).NotEmpty();
        RuleFor(c => c.UpdatedBy).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Validates the actor is identified.</summary>
public sealed class MarkRiskPolicyAsDefaultCommandValidator : AbstractValidator<MarkRiskPolicyAsDefaultCommand>
{
    public MarkRiskPolicyAsDefaultCommandValidator()
    {
        RuleFor(c => c.RiskPolicyId).NotEmpty();
        RuleFor(c => c.UpdatedBy).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="DeactivateRiskPolicyCommand"/>.</summary>
public sealed class DeactivateRiskPolicyCommandHandler(
    IRiskPolicyRepository policies, IUnitOfWork unitOfWork, IClock clock)
    : IRequestHandler<DeactivateRiskPolicyCommand, Result<RiskPolicyDto>>
{
    public async Task<Result<RiskPolicyDto>> HandleAsync(
        DeactivateRiskPolicyCommand command, CancellationToken cancellationToken)
    {
        var policy = await policies.GetByIdAsync(
            BuildingBlocks.Domain.Primitives.RiskPolicyId.From(command.RiskPolicyId), cancellationToken);

        if (policy is null)
        {
            return Result.Failure<RiskPolicyDto>(Error.NotFound(
                "risk.policy_not_found", $"No risk policy with id {command.RiskPolicyId} exists."));
        }

        // RiskPolicy.Deactivate itself throws a DomainException (mapped
        // globally to 422) when the target is the current default, rather
        // than this handler pre-checking and duplicating that rule.
        policy.Deactivate(clock.UtcNow, command.UpdatedBy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(RiskPolicyMapper.ToDto(policy));
    }
}

/// <summary>Handles <see cref="MarkRiskPolicyAsDefaultCommand"/>.</summary>
public sealed class MarkRiskPolicyAsDefaultCommandHandler(
    IRiskPolicyRepository policies, IUnitOfWork unitOfWork, IClock clock)
    : IRequestHandler<MarkRiskPolicyAsDefaultCommand, Result<RiskPolicyDto>>
{
    public async Task<Result<RiskPolicyDto>> HandleAsync(
        MarkRiskPolicyAsDefaultCommand command, CancellationToken cancellationToken)
    {
        var target = await policies.GetByIdAsync(
            BuildingBlocks.Domain.Primitives.RiskPolicyId.From(command.RiskPolicyId), cancellationToken);

        if (target is null)
        {
            return Result.Failure<RiskPolicyDto>(Error.NotFound(
                "risk.policy_not_found", $"No risk policy with id {command.RiskPolicyId} exists."));
        }

        if (!target.IsActive)
        {
            return Result.Failure<RiskPolicyDto>(Error.Conflict(
                "risk.policy.cannot_promote_inactive",
                "An inactive risk policy cannot be promoted to the default. Reactivate it first."));
        }

        var now = clock.UtcNow;

        foreach (var other in await policies.ListAsync(cancellationToken))
        {
            if (other.IsDefault && other.Id != target.Id)
            {
                other.DemoteFromDefault(now, command.UpdatedBy);
            }
        }

        target.MarkAsDefault(now, command.UpdatedBy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(RiskPolicyMapper.ToDto(target));
    }
}
