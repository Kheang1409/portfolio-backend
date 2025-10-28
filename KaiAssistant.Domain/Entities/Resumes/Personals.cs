namespace KaiAssistant.Domain.Entities.Resumes;

public record Personals
{
    public string LegalName { get; private set; } = string.Empty;
    public string PreferredName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string Linkedin { get; private set; } = string.Empty;
    public string Github { get; private set; } = string.Empty;
    public string Portfolio { get; private set; } = string.Empty;
}
