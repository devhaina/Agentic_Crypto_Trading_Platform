using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>An asset ticker, e.g. <c>BTC</c> or <c>USDT</c>. Normalised to upper case.</summary>
public readonly record struct AssetCode
{
    private AssetCode(string value) => Value = value;

    public string Value { get; }

    public static AssetCode Usdt => Create("USDT");

    /// <exception cref="DomainException">The code is empty or not alphanumeric.</exception>
    public static AssetCode Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("domain.asset.empty", "Asset code must not be empty.");
        }

        var normalised = value.Trim().ToUpperInvariant();

        foreach (var c in normalised)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                throw new DomainException(
                    "domain.asset.invalid_characters",
                    $"Asset code '{value}' must be alphanumeric.");
            }
        }

        return normalised.Length is < 2 or > 12
            ? throw new DomainException(
                "domain.asset.invalid_length", $"Asset code '{value}' has an implausible length.")
            : new AssetCode(normalised);
    }

    public override string ToString() => Value;

    public static implicit operator string(AssetCode code) => code.Value;
}
