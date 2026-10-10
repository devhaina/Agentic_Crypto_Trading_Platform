"""Configuration for the Agentiva AI agent platform.

Every setting arrives from the environment. The platform holds **no exchange
credentials** and has no setting that could introduce one: see
:mod:`agentiva_agents.policies.permissions` for the enforcement.
"""

from __future__ import annotations

from functools import lru_cache
from typing import Literal

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

LlmProviderName = Literal["stub", "anthropic", "openai"]


class Settings(BaseSettings):
    """Runtime settings, read from environment variables."""

    model_config = SettingsConfigDict(
        env_prefix="AI_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # --- Service identity -------------------------------------------------
    service_name: str = "agent-platform"
    environment: str = "local"
    log_level: str = "INFO"

    # --- LLM provider -----------------------------------------------------
    #
    # Defaults to "stub": a deterministic, rule-based provider that needs no
    # vendor account. That keeps the repository runnable out of the box and,
    # more usefully, makes the orchestrator's behaviour reproducible in tests —
    # an agent pipeline whose every run differs is one you cannot write an
    # assertion against.
    llm_provider: LlmProviderName = "stub"
    llm_model: str = "claude-sonnet-5"
    max_tokens: int = Field(default=4096, ge=256, le=200_000)
    temperature: float = Field(default=0.0, ge=0.0, le=1.0)

    anthropic_api_key: str = ""
    openai_api_key: str = ""

    # --- Timeouts ---------------------------------------------------------
    #
    # A bounded agent run matters: the orchestrator holds a request open while
    # agents work, and an unbounded model call would hold it indefinitely.
    agent_timeout_seconds: int = Field(default=60, ge=5, le=600)
    tool_timeout_seconds: int = Field(default=15, ge=1, le=120)

    # --- Platform access --------------------------------------------------
    #
    # The agents reach market and portfolio data only through the gateway's
    # read-only routes. There is no direct database or exchange access.
    gateway_base_url: str = "http://api-gateway:8080"
    gateway_timeout_seconds: int = Field(default=10, ge=1, le=60)

    # --- External read-only data sources -----------------------------------
    #
    # Both of these are outbound calls to third-party public APIs, not to the
    # platform's own services — still read-only, still no credential that
    # could touch an exchange. An empty news_api_key means get_news reports
    # "not_configured" rather than attempting a call that would only 401.
    news_api_key: str = ""
    news_base_url: str = "https://min-api.cryptocompare.com"
    onchain_base_url: str = "https://api.blockchain.info"

    # --- Infrastructure ---------------------------------------------------
    redis_url: str = ""
    rabbitmq_url: str = ""
    otlp_endpoint: str = ""

    @field_validator("log_level")
    @classmethod
    def _normalise_log_level(cls, value: str) -> str:
        allowed = {"DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"}
        upper = value.upper()
        if upper not in allowed:
            raise ValueError(f"log_level must be one of {sorted(allowed)}")
        return upper

    @property
    def llm_api_key(self) -> str:
        """The API key for the configured provider, or an empty string."""
        if self.llm_provider == "anthropic":
            return self.anthropic_api_key
        if self.llm_provider == "openai":
            return self.openai_api_key
        return ""

    @property
    def is_stub_mode(self) -> bool:
        """Whether the platform runs the deterministic provider.

        True when "stub" is configured, and also when a real provider is named
        but its key is absent. Falling back rather than failing to start keeps
        the service healthy and honest: it logs loudly, and a missing key is a
        configuration problem, not a reason for the platform to be down.
        """
        return self.llm_provider == "stub" or not self.llm_api_key


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Returns the cached settings instance."""
    return Settings()
