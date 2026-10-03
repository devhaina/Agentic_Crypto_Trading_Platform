using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Agentiva.BuildingBlocks.ServiceDefaults.Security;

/// <summary>JWT bearer validation settings.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key, for local development only.
    /// </summary>
    /// <remarks>
    /// A shared symmetric secret means every service that validates a token can
    /// also mint one, so any compromised service can forge an administrator
    /// identity. Acceptable while everything runs on one developer machine;
    /// production uses an OIDC provider with asymmetric keys, where services
    /// hold only the public key. See <c>docs/security/identity.md</c>.
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 bytes.")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Platform role names used in authorization policies.</summary>
public static class AgentivaRoles
{
    /// <summary>Full administrative access, including risk policy changes.</summary>
    public const string Administrator = "administrator";

    /// <summary>Can place and cancel orders.</summary>
    public const string Trader = "trader";

    /// <summary>Can operate the kill switch and acknowledge alerts.</summary>
    public const string Operator = "operator";

    /// <summary>Read-only access to dashboards and audit records.</summary>
    public const string Viewer = "viewer";
}

/// <summary>Authorization policy names.</summary>
public static class AgentivaPolicies
{
    /// <summary>Required to create a trading intent or cancel an order.</summary>
    public const string CanTrade = "can-trade";

    /// <summary>Required to engage or release the kill switch.</summary>
    public const string CanOperate = "can-operate";

    /// <summary>Required to change risk policies or exchange accounts.</summary>
    public const string CanAdminister = "can-administer";

    /// <summary>Required for any read access.</summary>
    public const string CanView = "can-view";
}

/// <summary>Registers JWT bearer authentication and the platform's policies.</summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>Adds token validation and role-based authorization policies.</summary>
    public static IServiceCollection AddAgentivaAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Tokens travel over TLS in every deployed environment. Allowed
                // over plain HTTP only on a developer machine, where the
                // compose network has no TLS terminator.
                options.RequireHttpsMetadata = !string.Equals(
                    configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            string.IsNullOrEmpty(jwt.SigningKey)
                                ? new string('0', 32)
                                : jwt.SigningKey)),

                    ValidateLifetime = true,

                    // No tolerance for an expired token. The default five
                    // minutes would let a revoked session keep trading for
                    // another five minutes, which is a long time in a market.
                    ClockSkew = TimeSpan.Zero,

                    RequireExpirationTime = true,
                    RequireSignedTokens = true
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AgentivaPolicies.CanView, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(
                    AgentivaRoles.Viewer,
                    AgentivaRoles.Trader,
                    AgentivaRoles.Operator,
                    AgentivaRoles.Administrator))
            .AddPolicy(AgentivaPolicies.CanTrade, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AgentivaRoles.Trader, AgentivaRoles.Administrator))
            .AddPolicy(AgentivaPolicies.CanOperate, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AgentivaRoles.Operator, AgentivaRoles.Administrator))
            .AddPolicy(AgentivaPolicies.CanAdminister, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AgentivaRoles.Administrator));

        return services;
    }
}
