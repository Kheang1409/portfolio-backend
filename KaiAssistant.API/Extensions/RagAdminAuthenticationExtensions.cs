using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KaiAssistant.API.Extensions;

public static class RagAdminAuthenticationExtensions
{
    public const string PolicyName = "RagAdmin";
    private const string RequiredRole = "RagAdmin";
    private const string ConfigurationSection = "RagAdminAuthentication";

    public static IServiceCollection AddRagAdminAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var authority = section["Authority"] ?? string.Empty;
        var audience = section["Audience"] ?? string.Empty;
        var configuredRoleClaimType = section["RoleClaimType"];
        var roleClaimType = string.IsNullOrWhiteSpace(configuredRoleClaimType) ? "role" : configuredRoleClaimType;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.RoleClaimType = roleClaimType;
            });

        services.AddAuthorization(options =>
            options.AddPolicy(PolicyName, policy =>
                policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireRole(RequiredRole)));

        return services;
    }
}
