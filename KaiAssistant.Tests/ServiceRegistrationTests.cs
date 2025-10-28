using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using KaiAssistant.Application.Interfaces;
using FluentAssertions;

namespace KaiAssistant.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void ServiceProvider_Resolves_EssentialServices()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["GeminiSettings:ApiKey"] = "test",
            ["GeminiSettings:ModelName"] = "test-model",
            ["GeminiSettings:Endpoint"] = "https://api.test/",
            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoDB:DatabaseName"] = "testdb",
            ["EmailSettings:SmtpServer"] = "smtp.test",
            ["EmailSettings:Port"] = "25",
            ["EmailSettings:SenderEmail"] = "from@test",
            ["EmailSettings:RecieverEmail"] = "to@test",
            ["EmailSettings:SenderPassword"] = "pass"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        var provider = services.BuildServiceProvider();

        var assistant = provider.GetService<IAssistantService>();
        assistant.Should().NotBeNull();

        var resumeRepo = provider.GetService<KaiAssistant.Domain.Interfaces.Repositories.IResumeRepository>();
        resumeRepo.Should().NotBeNull();
    }
}
