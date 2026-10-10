namespace Agentiva.Portfolio.Domain.Positions;

/// <summary>Which side of the market a position is on. Quantity stays unsigned; this carries direction.</summary>
public enum PositionDirection
{
    /// <summary>No open quantity.</summary>
    Flat = 1,

    Long = 2,

    Short = 3
}
