using System.Text;
using KaiAssistant.Domain.Entities;
using KaiAssistant.Application.Interfaces;
using OllamaSharp;

namespace KaiAssistant.Application.Services;

public class AssistantService : IAssistantService
{
    private readonly List<ResumeChunk> _resumeChunks = new();
    private readonly OllamaApiClient _ollamaClient;

    public AssistantService()
    {
        _ollamaClient = new OllamaApiClient(new Uri("http://localhost:11434"))
        {
            SelectedModel = "llama3"
        };
    }

    public void LoadResume(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Resume not found.", filePath);

        string resumeText = File.ReadAllText(filePath);
        _resumeChunks.Clear();

        int chunkSize = 500;
        for (int i = 0; i < resumeText.Length; i += chunkSize)
        {
            string chunk = resumeText.Substring(i, Math.Min(chunkSize, resumeText.Length - i));
            _resumeChunks.Add(new ResumeChunk { Content = chunk });
        }
    }

    public async Task<string> AskQuestionAsync(string question)
    {
        var contextChunks = FindRelevantChunks(question);
        string context = string.Join("\n---\n", contextChunks.Select(c => c.Content));

        var chat = new Chat(_ollamaClient, systemPrompt: @"
        You are Kai Taing's personal assistant.
        Only answer using the context provided.
        If unsure, say 'I don’t know.'");

        var fullPrompt = $"Context:\n{context}\n\nUser question:\n{question}";

        var sb = new StringBuilder();
        await foreach (var token in chat.SendAsync(fullPrompt))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    private List<ResumeChunk> FindRelevantChunks(string query)
    {
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return _resumeChunks
            .Select(c => new
            {
                Chunk = c,
                Score = queryWords.Count(word =>
                    c.Content.Contains(word, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(c => c.Score)
            .Take(2)
            .Select(c => c.Chunk)
            .ToList();
    }
}
