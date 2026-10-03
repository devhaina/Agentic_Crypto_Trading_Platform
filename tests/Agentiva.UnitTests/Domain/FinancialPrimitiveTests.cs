using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Domain;

/// <summary>
/// Tests for the financial value objects.
/// </summary>
/// <remarks>
/// These types exist to make specific, historically expensive mistakes
/// impossible. Each test names the mistake it prevents.
/// </remarks>
public sealed class PercentageTests
{
    /// <summary>
    /// The percent/fraction distinction is explicit in the API.
    /// </summary>
    /// <remarks>
    /// A bare decimal risk limit of <c>0.5</c> is ambiguous: half a percent or
    /// fifty percent? The two differ by a factor of one hundred, and reading it
    /// the wrong way risks 100x the intended amount per trade. Construction and
    /// access both state the unit, so the ambiguity cannot survive.
    /// </remarks>
    [Fact]
    public void Percent_and_fraction_constructors_are_not_interchangeable()
    {
        Percentage.FromPercent(0.5m).AsFraction.ShouldBe(0.005m);
        Percentage.FromFraction(0.5m).Percent.ShouldBe(50m);
    }

    [Fact]
    public void Applying_a_percentage_uses_the_fraction()
    {
        // Half a percent of 100,000 is 500, not 50,000.
        Percentage.FromPercent(0.5m).Of(100_000m).ShouldBe(500m);
    }

    /// <summary>
    /// Rejecting values above 100 catches the fraction/percent mix-up.
    /// </summary>
    [Fact]
    public void A_percentage_above_one_hundred_is_rejected()
    {
        var ex = Should.Throw<DomainException>(() => Percentage.FromPercent(150m));
        ex.Code.ShouldBe("domain.percentage.exceeds_one_hundred");
    }

    [Fact]
    public void A_negative_percentage_is_rejected()
        => Should.Throw<DomainException>(() => Percentage.FromPercent(-1m))
            .Code.ShouldBe("domain.percentage.negative");

    [Fact]
    public void A_fraction_above_one_is_rejected()
        => Should.Throw<DomainException>(() => Percentage.FromFraction(1.5m))
            .Code.ShouldBe("domain.percentage.fraction_out_of_range");

    [Fact]
    public void Round_trips_without_loss()
    {
        var original = Percentage.FromPercent(0.123456m);
        Percentage.FromFraction(original.AsFraction).Percent.ShouldBe(0.123456m);
    }
}

public sealed class MoneyTests
{
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");
    private static readonly AssetCode Btc = AssetCode.Create("BTC");

    /// <summary>
    /// Combining denominations is refused rather than silently producing a number.
    /// </summary>
    /// <remarks>
    /// Adding USDT to BTC is the classic route to a portfolio valuation that is
    /// confidently and invisibly wrong.
    /// </remarks>
    [Fact]
    public void Adding_different_currencies_is_rejected()
    {
        var usdt = Money.Create(100m, Usdt);
        var btc = Money.Create(1m, Btc);

        Should.Throw<DomainException>(() => usdt + btc)
            .Code.ShouldBe("domain.money.currency_mismatch");
    }

    [Fact]
    public void Comparing_different_currencies_is_rejected()
    {
        var usdt = Money.Create(100m, Usdt);
        var btc = Money.Create(1m, Btc);

        Should.Throw<DomainException>(() => usdt > btc);
    }

    [Fact]
    public void Money_may_be_negative_because_a_loss_is_a_real_value()
    {
        var loss = Money.Create(-250.50m, Usdt);

        loss.IsNegative.ShouldBeTrue();
        loss.Abs().Amount.ShouldBe(250.50m);
    }

    [Fact]
    public void Non_negative_factory_rejects_a_negative_amount()
        => Should.Throw<DomainException>(() => Money.CreateNonNegative(-1m, Usdt))
            .Code.ShouldBe("domain.money.negative");

    /// <summary>
    /// Decimal arithmetic must be exact where binary floating point is not.
    /// </summary>
    /// <remarks>
    /// With <c>double</c>, summing 0.1 ten times does not give 1.0. Accumulating
    /// fills that way drifts the recorded position away from the real one, and
    /// the drift is unbounded over a trading day.
    /// </remarks>
    [Fact]
    public void Repeated_addition_is_exact()
    {
        var total = Money.Zero(Usdt);

        for (var i = 0; i < 10; i++)
        {
            total += Money.Create(0.1m, Usdt);
        }

        total.Amount.ShouldBe(1.0m);
    }

    [Fact]
    public void Ratio_of_zero_total_is_zero_not_infinite()
    {
        var exposure = Money.Create(100m, Usdt);
        var empty = Money.Zero(Usdt);

        exposure.RatioOf(empty).ShouldBe(Percentage.Zero);
    }

    [Fact]
    public void Ratio_is_clamped_into_the_representable_band()
    {
        // Exposure above total value (leverage) clamps to 100% rather than
        // throwing, so a comparison against a limit still trips correctly.
        var exposure = Money.Create(200m, Usdt);
        var equity = Money.Create(100m, Usdt);

        exposure.RatioOf(equity).Percent.ShouldBe(100m);
    }
}

public sealed class QuantityAndPriceTests
{
    [Fact]
    public void A_negative_quantity_is_rejected()
        => Should.Throw<DomainException>(() => Quantity.Create(-1m))
            .Code.ShouldBe("domain.quantity.negative");

    [Fact]
    public void Subtraction_that_would_go_negative_is_rejected()
    {
        var held = Quantity.Create(1m);
        var sold = Quantity.Create(2m);

        Should.Throw<DomainException>(() => held - sold)
            .Code.ShouldBe("domain.quantity.negative_result");
    }

    [Fact]
    public void A_zero_or_negative_price_is_rejected()
    {
        Should.Throw<DomainException>(() => Price.Create(0m)).Code.ShouldBe("domain.price.not_positive");
        Should.Throw<DomainException>(() => Price.Create(-1m)).Code.ShouldBe("domain.price.not_positive");
    }

    [Fact]
    public void Price_distance_is_absolute()
    {
        Price.Create(100m).DistanceTo(Price.Create(90m)).ShouldBe(10m);
        Price.Create(90m).DistanceTo(Price.Create(100m)).ShouldBe(10m);
    }

    [Fact]
    public void Adverse_slippage_moves_against_the_trader()
    {
        var price = Price.Create(100m);
        var slippage = Percentage.FromPercent(1m);

        // A buy fills higher, a sell lower.
        price.WithAdverseSlippage(slippage, OrderSide.Buy).Value.ShouldBe(101m);
        price.WithAdverseSlippage(slippage, OrderSide.Sell).Value.ShouldBe(99m);
    }

    [Fact]
    public void Quantity_preserves_eighteen_decimal_places()
    {
        // An 18-decimal ERC-20 amount must survive intact.
        const decimal tiny = 0.000000000000000001m;
        Quantity.Create(tiny).Value.ShouldBe(tiny);
    }
}

public sealed class SymbolTests
{
    [Fact]
    public void Symbols_are_normalised_so_equivalent_markets_compare_equal()
    {
        // Without normalisation, "btcusdt" and "BTCUSDT" are different keys, a
        // position lookup misses, and the risk engine sees no exposure.
        Symbol.Create("btcusdt").ShouldBe(Symbol.Create("BTCUSDT"));
        Symbol.Create(" BTCUSDT ").Value.ShouldBe("BTCUSDT");
    }

    [Fact]
    public void Separators_are_rejected_rather_than_stripped()
    {
        // Stripping would map "BTC-USDT" and "BTCU-SDT" onto the same value,
        // masking a genuine mistake.
        Should.Throw<DomainException>(() => Symbol.Create("BTC-USDT"))
            .Code.ShouldBe("domain.symbol.invalid_characters");
    }

    [Fact]
    public void An_empty_symbol_is_rejected()
        => Should.Throw<DomainException>(() => Symbol.Create("  "))
            .Code.ShouldBe("domain.symbol.empty");

    [Fact]
    public void Try_create_does_not_throw_on_bad_input()
    {
        Symbol.TryCreate("BTC/USDT", out _).ShouldBeFalse();
        Symbol.TryCreate("BTCUSDT", out var symbol).ShouldBeTrue();
        symbol.Value.ShouldBe("BTCUSDT");
    }
}

public sealed class InstrumentPrecisionTests
{
    private static InstrumentPrecision Precision => new()
    {
        TickSize = 0.01m,
        StepSize = 0.001m,
        MinQuantity = 0.001m,
        MaxQuantity = 1000m,
        MinNotional = 10m,
        BaseAsset = AssetCode.Create("BTC"),
        QuoteAsset = AssetCode.Create("USDT")
    };

    /// <summary>
    /// Quantity rounding is always downward.
    /// </summary>
    /// <remarks>
    /// Rounding up could exceed the risk budget the size was derived from, or
    /// exceed the available balance and have the exchange reject the order.
    /// Rounding down can only risk less than intended.
    /// </remarks>
    [Fact]
    public void Quantity_is_floored_to_the_step_size()
    {
        Precision.NormalizeQuantity(Quantity.Create(0.0019m)).Value.ShouldBe(0.001m);
        Precision.NormalizeQuantity(Quantity.Create(0.0099m)).Value.ShouldBe(0.009m);
    }

    [Fact]
    public void Price_rounds_conservatively_for_the_side()
    {
        // A buy limit rounds down and a sell limit up, so neither ever pays
        // more per unit than the caller asked for.
        Precision.NormalizePrice(Price.Create(100.019m), OrderSide.Buy).Value.ShouldBe(100.01m);
        Precision.NormalizePrice(Price.Create(100.011m), OrderSide.Sell).Value.ShouldBe(100.02m);
    }

    [Fact]
    public void An_order_below_the_minimum_notional_is_rejected()
    {
        var result = Precision.ValidateOrder(Price.Create(100m), Quantity.Create(0.001m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("exchange.filter.min_notional");
    }

    [Fact]
    public void A_quantity_below_the_exchange_minimum_is_rejected()
    {
        var result = Precision.ValidateOrder(Price.Create(100_000m), Quantity.Create(0m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("exchange.filter.quantity_zero");
    }

    [Fact]
    public void A_misaligned_quantity_is_rejected()
    {
        var result = Precision.ValidateOrder(Price.Create(100_000m), Quantity.Create(0.0015m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("exchange.filter.step_size");
    }

    [Fact]
    public void A_valid_order_passes_every_filter()
    {
        var result = Precision.ValidateOrder(Price.Create(100_000m), Quantity.Create(0.001m));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Notional_is_price_times_quantity_in_the_quote_asset()
    {
        var notional = Precision.Notional(Price.Create(100_000m), Quantity.Create(0.01m));

        notional.Amount.ShouldBe(1_000m);
        notional.Currency.Value.ShouldBe("USDT");
    }
}
