namespace Agentiva.BuildingBlocks.ServiceDefaults.Configuration;

/// <summary>Identity of the running service, available via dependency injection.</summary>
/// <param name="Name">Logical service name, e.g. <c>trading-service</c>.</param>
/// <param name="DisplayName">Human-readable name for documentation and the dashboard.</param>
/// <param name="Version">Assembly version.</param>
/// <param name="Description">What the service is responsible for.</param>
public sealed record ServiceInfo(string Name, string DisplayName, string Version, string Description);
