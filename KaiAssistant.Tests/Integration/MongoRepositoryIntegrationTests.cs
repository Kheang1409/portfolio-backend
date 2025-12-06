using Mongo2Go;
using MongoDB.Driver;
using KaiAssistant.Infrastructure.Persistence;
using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace KaiAssistant.Tests.Integration;

[Trait("Category","Integration")]
public class MongoRepositoryIntegrationTests
{
    [Fact]
    public async Task MongoRepository_BasicCrud_Works_With_Session()
    {
        var runner = MongoDbRunner.Start();
        var client = new MongoClient(runner.ConnectionString);
        var db = client.GetDatabase("testdb");

        var services = new ServiceCollection();
        services.AddSingleton<IMongoClient>(client);
        services.AddSingleton(db);
        services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>));
        var sp = services.BuildServiceProvider();

        var repo = sp.GetRequiredService<IRepository<Resume>>();
        var resume = new Resume { Summary = "Test resume" };
        await repo.InsertAsync(resume);

        var all = await repo.GetAllAsync();
        Assert.NotEmpty(all);

        runner.Dispose();
    }
}