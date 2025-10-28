namespace KaiAssistant.Domain.Entities.Projects;

public record Project
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    private HashSet<string> _skills = new();
    private IReadOnlyCollection<string> Skills => _skills;
}
