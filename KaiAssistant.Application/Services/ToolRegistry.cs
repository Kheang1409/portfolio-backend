namespace KaiAssistant.Application.Services;
using global::KaiAssistant.Application.Interfaces;
using System.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolRegistry> _logger;
    private readonly Dictionary<string, IAssistantTool> _toolCache = new();
    private bool _initialized;
    public ToolRegistry(IServiceProvider serviceProvider, ILogger<ToolRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    public IAssistantTool? GetTool(string toolName)
    {
        EnsureInitialized();
        if (_toolCache.TryGetValue(toolName, out var tool))
        {
            return tool;
        }
        _logger.LogWarning("Tool not found: {ToolName}", toolName);
        return null;
    }
    public IEnumerable<IAssistantTool> GetAllTools()
    {
        EnsureInitialized();
        return _toolCache.Values;
    }
    public bool HasTool(string toolName)
    {
        EnsureInitialized();
        return _toolCache.ContainsKey(toolName);
    }
    public string GetFormattedToolManifest()
    {
        EnsureInitialized();
        if (_toolCache.Count == 0)
        {
            return "No tools available.";
        }
        var sb = new StringBuilder();
        sb.AppendLine("**Available Tools:**");
        sb.AppendLine();
        foreach (var (name, tool) in _toolCache)
        {
            sb.AppendLine($"- **{name}**: {tool.Description}");
        }
        return sb.ToString();
    }
    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        // Resolve all IAssistantTool implementations from DI container
        var toolTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(IAssistantTool).IsAssignableFrom(p) && p.IsClass && !p.IsAbstract);
        foreach (var toolType in toolTypes)
        {
            try
            {
                var instance = ActivatorUtilities.CreateInstance(_serviceProvider, toolType) as IAssistantTool;
                if (instance != null)
                {
                    _toolCache[instance.Name] = instance;
                    _logger.LogInformation("Registered tool: {ToolName}", instance.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register tool: {ToolType}", toolType.Name);
            }
        }
        _initialized = true;
    }
}