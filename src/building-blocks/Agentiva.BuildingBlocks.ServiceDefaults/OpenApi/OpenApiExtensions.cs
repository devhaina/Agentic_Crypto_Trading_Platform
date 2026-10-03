using Agentiva.BuildingBlocks.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Agentiva.BuildingBlocks.ServiceDefaults.OpenApi;

/// <summary>Registers Swagger/OpenAPI with the platform's conventions.</summary>
public static class OpenApiExtensions
{
    /// <summary>Current API version segment used in route templates.</summary>
    public const string ApiVersion = "v1";

    /// <summary>Adds the OpenAPI document generator and bearer security scheme.</summary>
    public static IServiceCollection AddAgentivaOpenApi(
        this IServiceCollection services,
        string serviceName,
        string displayName,
        string description,
        string version)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(ApiVersion, new OpenApiInfo
            {
                Title = $"Agentiva — {displayName}",
                Version = ApiVersion,
                Description = description
                              + "\n\nPart of the Agentiva agentic crypto trading platform. "
                              + "Financial quantities are transmitted as JSON **strings** to preserve "
                              + "decimal precision; parse them with a decimal type, never a float.",
                Contact = new OpenApiContact { Name = "Agentiva Platform Team" }
            });

            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT issued by the Agentiva Identity Service."
            };

            options.AddSecurityDefinition("Bearer", bearerScheme);

            // Microsoft.OpenApi v2 (which Swashbuckle 10 targets) replaced the
            // old inline-scheme-plus-OpenApiReference shape with a dedicated
            // reference type keyed directly in the requirement dictionary.
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document, null)] = new List<string>()
            });

            // Include XML doc comments so endpoint summaries reach Swagger UI.
            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{serviceName}.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            foreach (var xml in Directory.EnumerateFiles(AppContext.BaseDirectory, "Agentiva.*.xml"))
            {
                options.IncludeXmlComments(xml);
            }

            options.SupportNonNullableReferenceTypes();
            _ = version;
        });

        return services;
    }

    /// <summary>
    /// Serves the OpenAPI document and Swagger UI.
    /// </summary>
    /// <remarks>
    /// The JSON document is always served, because the gateway and the contract
    /// tests consume it. The interactive UI is served in every environment here
    /// so that the Phase 1 deliverable is explorable; a production deployment
    /// should gate it behind authentication or disable it, since it is a map of
    /// the entire API surface.
    /// </remarks>
    public static WebApplication UseAgentivaOpenApi(this WebApplication app, ServiceInfo serviceInfo)
    {
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint($"/openapi/{ApiVersion}.json", $"{serviceInfo.DisplayName} {ApiVersion}");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = $"Agentiva — {serviceInfo.DisplayName}";
            options.DisplayRequestDuration();
        });

        return app;
    }
}
