using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities.Resumes;
using KaiAssistant.Domain.Interfaces.Repositories;
using KaiAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Xunit;

#nullable enable

namespace KaiAssistant.Tests
{
    public class ResumeContextProviderTests
    {
        private class FakeResumeRepository : IResumeRepository
        {
            private readonly Resume? _resume;
            public FakeResumeRepository(Resume? resume) => _resume = resume;
            public Task<Resume?> GetByIdAsync(string id) => Task.FromResult<Resume?>(null);
            public Task<Resume?> GetLatestAsync() => Task.FromResult(_resume);
            public Task InsertAsync(Resume resume) => Task.CompletedTask;
        }

        private class NullLogger<T> : ILogger<T>
        {
            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
            private class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }

        [Fact]
        public async Task GetResumeChunksAsync_NoResume_ReturnsEmpty()
        {
            var repo = new FakeResumeRepository(null);
            var provider = new ResumeContextProvider(repo, new NullLogger<ResumeContextProvider>());

            var chunks = await provider.GetResumeChunksAsync(CancellationToken.None);
            chunks.Should().BeEmpty();

            var relevant = await provider.GetRelevantChunksAsync("any question", CancellationToken.None);
            relevant.Should().BeEmpty();
        }

        [Fact]
        public async Task GetResumeChunksAsync_WithSummaryAndSkills_ReturnsChunksAndRelevantPrioritizesSkills()
        {
            var resume = new Resume();
            resume.Summary = "Senior engineer with experience building web APIs and services.";
            resume.AddSkill("C#");
            resume.AddSkill("ASP.NET Core");

            var repo = new FakeResumeRepository(resume);
            var provider = new ResumeContextProvider(repo, new NullLogger<ResumeContextProvider>());

            var chunks = await provider.GetResumeChunksAsync(CancellationToken.None);
            chunks.Should().NotBeEmpty();
            chunks.Select(c => c.Label).Should().Contain("Summary");
            chunks.Select(c => c.Label).Should().Contain("Skills");

            var relevant = await provider.GetRelevantChunksAsync("What programming skills and technologies does Kai know?", CancellationToken.None);
            relevant.Should().NotBeEmpty();
            relevant[0].Label.Should().Be("Skills");
        }

        [Fact]
        public async Task GetResumeChunksAsync_LongSummary_IsChunked()
        {
            var longSummary = string.Concat(Enumerable.Repeat("abcdefghij", 100)); // ~1000 chars
            var resume = new Resume { Summary = longSummary };
            var repo = new FakeResumeRepository(resume);
            var provider = new ResumeContextProvider(repo, new NullLogger<ResumeContextProvider>());

            var chunks = await provider.GetResumeChunksAsync(CancellationToken.None);
            chunks.Length.Should().BeGreaterThan(1);
            chunks.All(c => c.Content.Length <= 500).Should().BeTrue();
        }
    }
}
