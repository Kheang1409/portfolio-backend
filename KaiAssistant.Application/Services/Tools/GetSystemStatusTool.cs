namespace KaiAssistant.Application.Services.Tools;
using global::KaiAssistant.Application.Interfaces;
using System.Diagnostics;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
public sealed class GetSystemStatusTool : IAssistantTool
{
    private readonly ILogger<GetSystemStatusTool> _logger;
    private readonly IAssistantOrchestrator _orchestrator;
    public string Name => "get_system_status";
    public string Description =>
        "Retrieves system health status including cache metrics, AI latency, active conversations, " +
        "and rate limiting stats. Use this to answer questions about system performance and availability.";
    public string InputSchema => JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            detailed = new
            {
                type = "boolean",
                description = "Return detailed metrics (default: false)"
            }
        }
    });
    public GetSystemStatusTool(
        ILogger<GetSystemStatusTool> logger,
        IAssistantOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }
    public bool ValidateArguments(Dictionary<string, object> args)
    {
        if (args.TryGetValue("detailed", out var detailedObj) && detailedObj is not bool)
            return false;
        return true;
    }
    public async Task<object> ExecuteAsync(Dictionary<string, object> args, CancellationToken cancellationToken = default)
    {
        try
        {
            var detailed = args.TryGetValue("detailed", out var d) && d is bool b && b;
            var diagnostics = await _orchestrator.GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var status = new
            {
                timestamp = DateTimeOffset.UtcNow,
                status = diagnostics.LastLatencyMs < 5000 ? "healthy" : "degraded",
                metrics = new
                {
                    lastLatencyMs = diagnostics.LastLatencyMs,
                    cacheHitRate = Math.Round(diagnostics.CacheHitRate * 100, 2),
                    activeStreams = diagnostics.ActiveStreamCount,
                    lastAiCall = diagnostics.LastAiCallAt
                }
            };
            if (detailed)
            {
                // Add memory and process info
                var process = Process.GetCurrentProcess();
                ((dynamic)status).detailed = new
                {
                    memoryMb = process.WorkingSet64 / (1024 * 1024),
                    threads = process.Threads.Count,
                    cpuPercent = GetCpuPercentage(process)
                };
            }
            _logger.LogInformation("System status retrieved: detailed={Detailed}", detailed);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetSystemStatusTool");
            throw;
        }
    }
    private static double GetCpuPercentage(Process process)
    {
        var startTime = DateTime.UtcNow;
        var startCpuUsage = process.TotalProcessorTime;
        Thread.Sleep(500);
        var endTime = DateTime.UtcNow;
        var endCpuUsage = process.TotalProcessorTime;
        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
        return cpuUsageTotal * 100;
    }
}