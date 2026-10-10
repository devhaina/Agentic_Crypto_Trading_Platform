"""Composition root for the agent platform."""

from __future__ import annotations

from dataclasses import dataclass

import structlog

from agentiva_agents.agents.analysis_agents import (
    MarketAgent,
    PortfolioAgent,
    ResearchAgent,
    SentimentAgent,
    TechnicalAgent,
)
from agentiva_agents.agents.strategy_agent import StrategyAgent
from agentiva_agents.config import Settings
from agentiva_agents.llm.anthropic_provider import create_provider
from agentiva_agents.llm.provider import LlmProvider
from agentiva_agents.orchestrator.graph import AgentOrchestrator
from agentiva_agents.tools.market_tools import PlatformReadClient, register_market_tools
from agentiva_agents.tools.news_tools import NewsClient, register_news_tools
from agentiva_agents.tools.onchain_tools import OnChainClient, register_onchain_tools
from agentiva_agents.tools.registry import ToolRegistry

logger = structlog.get_logger(__name__)


@dataclass(slots=True)
class PlatformContext:
    """Long-lived objects built once at startup."""

    settings: Settings
    registry: ToolRegistry
    provider: LlmProvider
    orchestrator: AgentOrchestrator
    read_client: PlatformReadClient
    news_client: NewsClient
    onchain_client: OnChainClient

    async def aclose(self) -> None:
        """Releases every HTTP client on shutdown."""
        await self.read_client.aclose()
        await self.news_client.aclose()
        await self.onchain_client.aclose()


def build_platform(settings: Settings) -> PlatformContext:
    """Builds the tool registry, provider and orchestrator.

    The tool registry is built and sealed here, at startup. Every registration
    runs the permission gate, so a tool that breaches the AI trust boundary
    stops the process from starting rather than being discovered later.
    """
    read_client = PlatformReadClient(settings)
    news_client = NewsClient(settings)
    onchain_client = OnChainClient(settings)

    registry = ToolRegistry()
    register_market_tools(registry, read_client)
    register_news_tools(registry, news_client)
    register_onchain_tools(registry, onchain_client)

    # Belt and braces: verifies the whole registered set against the allow-list,
    # catching anything added by a path that bypassed register().
    registry.assert_complete()
    registry.seal()

    provider = create_provider(
        provider_name=settings.llm_provider,
        api_key=settings.llm_api_key,
        model=settings.llm_model,
    )

    if settings.is_stub_mode:
        logger.warning(
            "running_in_stub_mode",
            reason="no LLM provider configured or no API key supplied",
            detail=(
                "Agent analysis is deterministic and rule-based. Proposals are still fully "
                "risk-gated downstream."
            ),
        )

    evidence_agents = [
        MarketAgent(provider, registry),
        TechnicalAgent(provider, registry),
        SentimentAgent(provider, registry),
        PortfolioAgent(provider, registry),
    ]

    orchestrator = AgentOrchestrator(
        evidence_agents=evidence_agents,  # type: ignore[arg-type]
        strategy_agent=StrategyAgent(provider, registry),
        timeout_seconds=settings.agent_timeout_seconds,
    )

    logger.info(
        "platform_built",
        provider=provider.model_name,
        tools=registry.names,
        evidence_agents=[agent.name for agent in evidence_agents],
    )

    return PlatformContext(
        settings=settings,
        registry=registry,
        provider=provider,
        orchestrator=orchestrator,
        read_client=read_client,
        news_client=news_client,
        onchain_client=onchain_client,
    )


def build_research_agent(context: PlatformContext) -> ResearchAgent:
    """Builds the research agent, which runs outside the proposal pipeline.

    Kept separate because it analyses historical performance rather than
    contributing to a trade decision, and must never influence one.
    """
    return ResearchAgent(context.provider, context.registry)
