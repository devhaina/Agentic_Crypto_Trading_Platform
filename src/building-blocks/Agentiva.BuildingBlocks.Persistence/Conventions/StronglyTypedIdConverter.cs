using System.Linq.Expressions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agentiva.BuildingBlocks.Persistence.Conventions;

/// <summary>
/// Maps a strongly typed identifier to the <see cref="Guid"/> column that stores it.
/// </summary>
/// <typeparam name="TId">
/// The identifier type: a value type with a <c>Guid Value</c> property and a
/// constructor taking a single <see cref="Guid"/>, which every
/// <see cref="IStronglyTypedId{TSelf}"/> record struct satisfies.
/// </typeparam>
/// <remarks>
/// <para>
/// The conversion expressions are built with the <see cref="Expression"/> API
/// rather than written as lambdas. The natural formulation —
/// <c>base(id =&gt; id.Value, value =&gt; TId.From(value))</c> — does not compile:
/// <see cref="ValueConverter{TModel,TProvider}"/> takes expression trees so that
/// EF can translate them into SQL, and C# forbids an expression tree from
/// calling a static abstract interface member such as <c>TId.From</c>
/// (CS8927). Reflecting onto the constructor sidesteps that while keeping a
/// public parameterless constructor, which is what
/// <c>ModelConfigurationBuilder.HaveConversion(Type)</c> requires.
/// </para>
/// <para>
/// Both expressions are built once per closed generic type, at first use.
/// </para>
/// </remarks>
public sealed class StronglyTypedIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct
{
    public StronglyTypedIdConverter()
        : base(BuildToProviderExpression(), BuildFromProviderExpression())
    {
    }

    private static Expression<Func<TId, Guid>> BuildToProviderExpression()
    {
        var property = typeof(TId).GetProperty(nameof(IStronglyTypedId<UserId>.Value))
                       ?? throw new InvalidOperationException(
                           $"{typeof(TId).Name} has no public Value property and cannot be used as an identifier.");

        var parameter = Expression.Parameter(typeof(TId), "id");
        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(parameter, property), parameter);
    }

    private static Expression<Func<Guid, TId>> BuildFromProviderExpression()
    {
        var constructor = typeof(TId).GetConstructor([typeof(Guid)])
                          ?? throw new InvalidOperationException(
                              $"{typeof(TId).Name} has no constructor taking a single Guid and cannot be "
                              + "used as an identifier.");

        var parameter = Expression.Parameter(typeof(Guid), "value");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, parameter), parameter);
    }
}

/// <summary>Maps a <see cref="Symbol"/> to its normalised text.</summary>
public sealed class SymbolConverter : ValueConverter<Symbol, string>
{
    public SymbolConverter()
        : base(symbol => symbol.Value, value => Symbol.Create(value))
    {
    }
}

/// <summary>Maps an <see cref="AssetCode"/> to its normalised text.</summary>
public sealed class AssetCodeConverter : ValueConverter<AssetCode, string>
{
    public AssetCodeConverter()
        : base(asset => asset.Value, value => AssetCode.Create(value))
    {
    }
}

/// <summary>Maps a <see cref="Quantity"/> to its decimal column.</summary>
public sealed class QuantityConverter : ValueConverter<Quantity, decimal>
{
    public QuantityConverter()
        : base(quantity => quantity.Value, value => Quantity.Create(value))
    {
    }
}

/// <summary>Maps a <see cref="Price"/> to its decimal column.</summary>
public sealed class PriceConverter : ValueConverter<Price, decimal>
{
    public PriceConverter()
        : base(price => price.Value, value => Price.Create(value))
    {
    }
}

/// <summary>
/// Maps a <see cref="Percentage"/> to a decimal column holding percent units.
/// </summary>
/// <remarks>
/// Stored in percent units, matching <see cref="Percentage.Percent"/>, so a
/// value read directly in SQL means what it appears to mean. Storing the
/// fraction instead would make every ad-hoc query a place to reintroduce the
/// hundred-fold mistake the type exists to prevent.
/// </remarks>
public sealed class PercentageConverter : ValueConverter<Percentage, decimal>
{
    public PercentageConverter()
        : base(percentage => percentage.Percent, value => Percentage.FromPercent(value))
    {
    }
}
