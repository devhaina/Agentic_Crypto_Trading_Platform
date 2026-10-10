"""Symbol parsing shared by external-data tools.

Trading pairs arrive as the platform's own convention (``BTCUSDT``), but every
third-party API indexes by the base asset alone (``BTC``). This is the one
place that convention is decoded, so news and on-chain tools agree with each
other and with any future external-data tool on how it is done.
"""

from __future__ import annotations

_QUOTE_ASSETS = ("USDT", "USDC", "BUSD", "USD", "EUR", "BTC", "ETH")


def base_asset_of(symbol: str) -> str:
    """Returns the base asset of a trading pair, e.g. ``BTCUSDT`` -> ``BTC``.

    Falls back to the symbol unchanged if no known quote asset suffix
    matches — callers treat an unrecognised base asset as "unavailable"
    rather than guessing.
    """
    upper = symbol.upper()
    for quote in _QUOTE_ASSETS:
        if upper.endswith(quote) and len(upper) > len(quote):
            return upper[: -len(quote)]
    return upper
