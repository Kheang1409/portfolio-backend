using System.Security.Claims;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using KaiAssistant.API.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KaiAssistant.Tests;

public sealed class RagAdminAuthenticationTests
{
    [Fact]
    public async Task Policy_requires_authenticated_rag_admin_role_and_configures_jwt_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RagAdminAuthentication:Authority"] = "https://identity.example.test/",
                ["RagAdminAuthentication:Audience"] = "kaiassistant-api",
                ["RagAdminAuthentication:RoleClaimType"] = "roles"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRagAdminAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        jwtOptions.Authority.Should().Be("https://identity.example.test/");
        jwtOptions.Audience.Should().Be("kaiassistant-api");
        jwtOptions.MapInboundClaims.Should().BeFalse();
        jwtOptions.TokenValidationParameters.RoleClaimType.Should().Be("roles");

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", "RagAdmin")],
            JwtBearerDefaults.AuthenticationScheme,
            "sub",
            "roles"));
        var visitorPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", "Visitor")],
            JwtBearerDefaults.AuthenticationScheme,
            "sub",
            "roles"));
        var anonymousPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        (await authorization.AuthorizeAsync(adminPrincipal, null, RagAdminAuthenticationExtensions.PolicyName))
            .Succeeded.Should().BeTrue();
        (await authorization.AuthorizeAsync(visitorPrincipal, null, RagAdminAuthenticationExtensions.PolicyName))
            .Succeeded.Should().BeFalse();
        (await authorization.AuthorizeAsync(anonymousPrincipal, null, RagAdminAuthenticationExtensions.PolicyName))
            .Succeeded.Should().BeFalse();
    }
}
