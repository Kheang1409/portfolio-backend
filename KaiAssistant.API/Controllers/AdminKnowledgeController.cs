using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Options;
using KaiAssistant.Domain.Entities.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Security.Cryptography;

namespace KaiAssistant.API.Controllers;

[ApiController, Authorize(Policy = "RagAdmin"), Route("api/admin/knowledge")]
public sealed class AdminKnowledgeController(IKnowledgeDocumentStore documents, IIngestionJobStore jobs, IDocumentStorage storage,
    IKnowledgeReindexService reindex, IOptions<RagIngestionOptions> options) : ControllerBase
{
    [HttpPost("documents"), RequestSizeLimit(5_500_000)]
    public async Task<IActionResult> Upload([FromForm] KnowledgeUploadRequest request, CancellationToken cancellationToken)
    {
        var config = options.Value;
        var file = request.File;
        var title = request.Title;
        var source = request.Source;
        var documentType = request.DocumentType;
        var tags = request.Tags;
        var version = request.Version;
        if (file is null || file.Length == 0) return Problem(statusCode: 400, title: "A document file is required.");
        if (file.Length > config.MaxFileSizeBytes) return Problem(statusCode: 413, title: "Document exceeds the configured size limit.");
        if (!config.AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return Problem(statusCode: 415, title: "Unsupported document content type.");
        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 200 || title?.Length > 200 || source?.Length > 500 || tags?.Length > 500) return Problem(statusCode: 400, title: "Invalid document metadata.");
        string hash;
        await using (var hashStream = file.OpenReadStream()) { hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant(); }
        var existing = await documents.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Accepted($"/api/admin/knowledge/documents/{existing.Id}", new { documentId = existing.Id, status = existing.Status, duplicate = true });
        }
        await using var upload = file.OpenReadStream();
        var key = await storage.StoreAsync(upload, safeName, cancellationToken).ConfigureAwait(false);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(documentType)) metadata["documentType"] = documentType;
        if (!string.IsNullOrWhiteSpace(tags)) metadata["tags"] = tags;
        if (!string.IsNullOrWhiteSpace(version)) metadata["version"] = version;
        var document = new KnowledgeDocument { Id = ObjectId.GenerateNewId().ToString(), Source = source ?? safeName, Title = title ?? safeName, MimeType = file.ContentType, ContentHash = hash, StorageKey = key, Status = "Pending", Metadata = metadata };
        await documents.AddAsync(document, cancellationToken).ConfigureAwait(false);
        var job = new IngestionJob { Id = ObjectId.GenerateNewId().ToString(), DocumentId = document.Id!, MaxAttempts = config.MaxAttempts, CreatedBy = User.Identity?.Name, CorrelationId = HttpContext.TraceIdentifier };
        await jobs.AddAsync(job, cancellationToken).ConfigureAwait(false);
        return Accepted($"/api/admin/knowledge/jobs/{job.Id}", new { documentId = document.Id, jobId = job.Id, status = "queued", statusUrl = $"/api/admin/knowledge/jobs/{job.Id}" });
    }
    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetJob(string jobId, CancellationToken cancellationToken) => (await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false)) is { } job ? Ok(new { job.Id, job.DocumentId, status = job.Status.ToString(), job.AttemptCount, job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc, job.LastAttemptAtUtc, job.ErrorCode, job.ErrorMessage }) : NotFound();
    [HttpGet("documents/{documentId}")]
    public async Task<IActionResult> GetDocument(string documentId, CancellationToken cancellationToken) => (await documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false)) is { } document ? Ok(new { document.Id, document.Title, document.Source, document.MimeType, document.Status, document.Version, document.ChunkCount, document.CreatedAtUtc, document.UpdatedAtUtc }) : NotFound();
    [HttpPost("jobs/{jobId}/cancel")]
    public async Task<IActionResult> Cancel(string jobId, CancellationToken cancellationToken) => await jobs.CancelAsync(jobId, cancellationToken).ConfigureAwait(false) ? Ok(new { jobId, status = "cancelled" }) : Conflict(new { title = "Only queued jobs may be cancelled." });
    [HttpPost("indexes/stage")]
    public async Task<IActionResult> StageIndex(CancellationToken cancellationToken) => Ok(await reindex.StageAllAsync(cancellationToken).ConfigureAwait(false));
    [HttpPost("indexes/activate")]
    public async Task<IActionResult> ActivateIndex(CancellationToken cancellationToken) { await reindex.ActivateStagedAsync(cancellationToken).ConfigureAwait(false); return Ok(new { status = "activated" }); }
    [HttpPost("indexes/rollback")]
    public async Task<IActionResult> RollbackIndex(CancellationToken cancellationToken) { await reindex.RollbackAsync(cancellationToken).ConfigureAwait(false); return Ok(new { status = "rolled-back" }); }
}

public sealed class KnowledgeUploadRequest
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Source { get; set; }
    public string? DocumentType { get; set; }
    public string? Tags { get; set; }
    public string? Version { get; set; }
}
