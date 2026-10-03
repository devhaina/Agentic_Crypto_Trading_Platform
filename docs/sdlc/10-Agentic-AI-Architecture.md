# Agentic AI Architecture

## Agent Hierarchy

```text
Agent Orchestrator
  |
  +-- Market Agent
  +-- Technical Agent
  +-- Sentiment Agent
  +-- Strategy Agent
  +-- Portfolio Agent
  +-- Research Agent
```

## Agent Permissions

Read-only:
- market data
- indicators
- portfolio
- positions
- strategy performance
- approved news/sentiment sources

Forbidden:
- place order
- cancel order
- withdraw funds
- transfer funds
- modify credentials
- modify production risk limits

## Agent State
Use typed Pydantic state:
- symbol
- timeframe
- market_data
- indicators
- sentiment
- strategy_signals
- portfolio
- risk_context
- proposal

## Orchestration
Market analysis -> technical analysis -> sentiment -> strategy synthesis -> proposal -> deterministic risk gate.

## LLM Output
Use JSON schema validation. Store model name, prompt/version reference, token usage, tool calls, output and decision.

## AI Safety
- No unrestricted tools.
- No secret access.
- No direct exchange access.
- Fail closed if required evidence is missing.
- Timeouts on every model/tool call.
- Store agent run IDs for audit.
