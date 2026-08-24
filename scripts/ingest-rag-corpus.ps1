[CmdletBinding()]
param(
    [string]$BaseUrl = $env:KAIASSISTANT_BASE_URL,
    [string]$ManifestPath = '',
    [int]$PollSeconds = 2,
    [int]$TimeoutMinutes = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BaseUrl)) { throw 'Set KAIASSISTANT_BASE_URL to the API base URL.' }
$token = $env:RAG_ADMIN_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Set RAG_ADMIN_TOKEN to a token with the RagAdmin role. Tokens are never printed.' }

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $scriptRoot '..\docs\rag-corpus-manifest.json' }
$manifestFullPath = if ([IO.Path]::IsPathRooted($ManifestPath)) { [IO.Path]::GetFullPath($ManifestPath) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location) $ManifestPath)) }
$manifest = Get-Content -Raw -LiteralPath $manifestFullPath | ConvertFrom-Json
if (@($manifest).Count -eq 0) { throw 'The corpus manifest is empty.' }
$ids = @($manifest | ForEach-Object id)
if ($ids.Count -ne (@($ids | Sort-Object -Unique)).Count) { throw 'The corpus manifest contains duplicate IDs.' }

$root = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes($TimeoutMinutes)
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$results = [Collections.Generic.List[object]]::new()

try {
    foreach ($item in @($manifest | Where-Object enabled)) {
        $relativePath = [IO.Path]::GetFullPath((Join-Path $root $item.path))
        if (-not $relativePath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escapes the repository: $($item.id)" }
        if (-not (Test-Path -LiteralPath $relativePath -PathType Leaf)) { throw "Manifest source is missing: $($item.id)" }
        $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
        $mime = switch ($extension) { '.md' { 'text/markdown' } '.txt' { 'text/plain' } '.json' { 'application/json' } default { throw "Unsupported corpus type for $($item.id): $extension" } }

        $form = [Net.Http.MultipartFormDataContent]::new()
        $stream = [IO.File]::OpenRead($relativePath)
        try {
            $fileContent = [Net.Http.StreamContent]::new($stream)
            $fileContent.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::Parse($mime)
            $form.Add($fileContent, 'file', [IO.Path]::GetFileName($relativePath))
            $form.Add([Net.Http.StringContent]::new([string]$item.title), 'title')
            $form.Add([Net.Http.StringContent]::new([string]$item.source), 'source')
            $form.Add([Net.Http.StringContent]::new([string]$item.documentType), 'documentType')
            $form.Add([Net.Http.StringContent]::new(([string[]]$item.tags -join ',')), 'tags')
            $form.Add([Net.Http.StringContent]::new([string]$item.version), 'version')
            $response = $client.PostAsync("$($BaseUrl.TrimEnd('/'))/api/admin/knowledge/documents", $form).GetAwaiter().GetResult()
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) { throw "Upload failed for $($item.id) with HTTP $([int]$response.StatusCode)." }
            $accepted = $body | ConvertFrom-Json
            $jobId = $accepted.jobId
            if ([string]::IsNullOrWhiteSpace($jobId)) {
                $results.Add([pscustomobject]@{ id=$item.id; status='Duplicate'; documentId=$accepted.documentId; jobId=$null; durationMs=0; chunkCount=$null })
                continue
            }
        }
        finally { $stream.Dispose(); $form.Dispose() }

        $started = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Seconds $PollSeconds
            $jobResponse = $client.GetAsync("$($BaseUrl.TrimEnd('/'))/api/admin/knowledge/jobs/$jobId").GetAwaiter().GetResult()
            if (-not $jobResponse.IsSuccessStatusCode) { throw "Job lookup failed for $($item.id) with HTTP $([int]$jobResponse.StatusCode)." }
            $job = $jobResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        } while ($job.status -in @('Queued', 'Processing'))
        if ($job.status -notin @('Completed', 'Duplicate')) { throw "Ingestion did not complete for $($item.id): $($job.status)." }
        $document = $client.GetAsync("$($BaseUrl.TrimEnd('/'))/api/admin/knowledge/documents/$($job.documentId)").GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $results.Add([pscustomobject]@{ id=$item.id; status=$job.status; documentId=$job.documentId; jobId=$jobId; durationMs=[int]$started.Elapsed.TotalMilliseconds; chunkCount=$document.chunkCount })
    }
}
finally { $client.Dispose() }

$results | ConvertTo-Json -Depth 4
