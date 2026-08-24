using KaiAssistant.Domain.Entities.AI;

namespace KaiAssistant.Application.Interfaces;

public interface IDocumentStorage
{
    Task<string> StoreAsync(Stream content, string safeFileName, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}
public interface IDocumentParser { bool CanParse(string mimeType); Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default); }
public interface IIngestionJobStore
{
    Task AddAsync(IngestionJob job, CancellationToken cancellationToken = default);
    Task<IngestionJob?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IngestionJob?> ClaimAsync(string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken = default);
    Task<bool> RenewLeaseAsync(string id, string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken = default);
    Task CompleteAsync(string id, string workerId, CancellationToken cancellationToken = default);
    Task FailAsync(string id, string workerId, string code, string message, DateTimeOffset nextAttempt, bool deadLetter, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default);
}
public interface IKnowledgeDocumentStore
{
    Task<KnowledgeDocument?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<KnowledgeDocument?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);
    Task SetStatusAsync(string id, string status, int chunkCount = 0, CancellationToken cancellationToken = default);
}
