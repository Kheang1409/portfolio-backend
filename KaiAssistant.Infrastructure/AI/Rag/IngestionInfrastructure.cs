using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text;

namespace KaiAssistant.Infrastructure.AI.Rag;

public sealed class LocalDocumentStorage(IHostEnvironment environment) : IDocumentStorage
{
    private readonly string _root = Path.Combine(environment.ContentRootPath, "App_Data", "knowledge");
    public async Task<string> StoreAsync(Stream content, string safeFileName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root); var key = $"{Guid.NewGuid():N}-{safeFileName}"; var path = Path.Combine(_root, key);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false); return key;
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, Path.GetFileName(key));
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}
public sealed class TextDocumentParser : IDocumentParser
{
    public bool CanParse(string mimeType) => mimeType is "text/plain" or "text/markdown" or "application/json";
    public async Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default)
    { using var reader = new StreamReader(stream, Encoding.UTF8, true, 81920, leaveOpen: true); return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false); }
}
public sealed class MongoKnowledgeDocumentStore(IMongoDatabase database) : IKnowledgeDocumentStore
{
    private readonly IMongoCollection<KnowledgeDocument> _documents = database.GetCollection<KnowledgeDocument>("knowledge_base_documents");
    public Task<KnowledgeDocument?> GetAsync(string id, CancellationToken ct = default) => _documents.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
    public Task<KnowledgeDocument?> FindByHashAsync(string hash, CancellationToken ct = default) => _documents.Find(x => x.ContentHash == hash).FirstOrDefaultAsync(ct);
    public Task AddAsync(KnowledgeDocument document, CancellationToken ct = default) => _documents.InsertOneAsync(document, cancellationToken: ct);
    public Task SetStatusAsync(string id, string status, int chunkCount = 0, CancellationToken ct = default) => _documents.UpdateOneAsync(x => x.Id == id, Builders<KnowledgeDocument>.Update.Set(x => x.Status, status).Set(x => x.ChunkCount, chunkCount).Set(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), cancellationToken: ct);
}
public sealed class MongoIngestionJobStore(IMongoDatabase database) : IIngestionJobStore
{
    private readonly IMongoCollection<IngestionJob> _jobs = database.GetCollection<IngestionJob>("ingestion_jobs");
    public Task AddAsync(IngestionJob job, CancellationToken ct = default) => _jobs.InsertOneAsync(job, cancellationToken: ct);
    public Task<IngestionJob?> GetAsync(string id, CancellationToken ct = default) => _jobs.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
    public Task<IngestionJob?> ClaimAsync(string worker, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default)
    {
        var eligible = (Builders<IngestionJob>.Filter.Eq(x => x.Status, IngestionJobStatus.Queued) & Builders<IngestionJob>.Filter.Lte(x => x.NextAttemptAtUtc, now)) |
            (Builders<IngestionJob>.Filter.Eq(x => x.Status, IngestionJobStatus.Processing) & Builders<IngestionJob>.Filter.Lte(x => x.LeaseExpiresAtUtc, now));
        var update = Builders<IngestionJob>.Update.Set(x => x.Status, IngestionJobStatus.Processing).Set(x => x.WorkerId, worker).Set(x => x.LeaseExpiresAtUtc, now.Add(lease)).Set(x => x.StartedAtUtc, now).Set(x => x.LastAttemptAtUtc, now).Inc(x => x.AttemptCount, 1);
        return _jobs.FindOneAndUpdateAsync(eligible, update, new FindOneAndUpdateOptions<IngestionJob> { Sort = Builders<IngestionJob>.Sort.Ascending(x => x.CreatedAtUtc), ReturnDocument = ReturnDocument.After }, ct);
    }
    public async Task<bool> RenewLeaseAsync(string id, string worker, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default) =>
        (await _jobs.UpdateOneAsync(x => x.Id == id && x.Status == IngestionJobStatus.Processing && x.WorkerId == worker && x.LeaseExpiresAtUtc >= now,
            Builders<IngestionJob>.Update.Set(x => x.LeaseExpiresAtUtc, now.Add(lease)), cancellationToken: ct).ConfigureAwait(false)).ModifiedCount == 1;
    public Task CompleteAsync(string id, string worker, CancellationToken ct = default) => _jobs.UpdateOneAsync(x => x.Id == id && x.WorkerId == worker, Builders<IngestionJob>.Update.Set(x => x.Status, IngestionJobStatus.Completed).Set(x => x.CompletedAtUtc, DateTimeOffset.UtcNow).Set(x => x.LeaseExpiresAtUtc, null), cancellationToken: ct);
    public Task FailAsync(string id, string worker, string code, string message, DateTimeOffset next, bool dead, CancellationToken ct = default) => _jobs.UpdateOneAsync(x => x.Id == id && x.WorkerId == worker, Builders<IngestionJob>.Update.Set(x => x.Status, dead ? IngestionJobStatus.DeadLetter : IngestionJobStatus.Queued).Set(x => x.ErrorCode, code).Set(x => x.ErrorMessage, message[..Math.Min(500, message.Length)]).Set(x => x.NextAttemptAtUtc, next).Set(x => x.LeaseExpiresAtUtc, null), cancellationToken: ct);
    public async Task<bool> CancelAsync(string id, CancellationToken ct = default) => (await _jobs.UpdateOneAsync(x => x.Id == id && x.Status == IngestionJobStatus.Queued, Builders<IngestionJob>.Update.Set(x => x.Status, IngestionJobStatus.Cancelled), cancellationToken: ct).ConfigureAwait(false)).ModifiedCount > 0;
}
