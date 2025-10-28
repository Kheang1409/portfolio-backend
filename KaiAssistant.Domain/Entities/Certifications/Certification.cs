namespace KaiAssistant.Domain.Entities.Certifications;

public class Certification
{
    public string Title { get; private set; } = string.Empty;
    public string Issuer { get; private set; } = string.Empty;
    public DateTime? Date { get; private set; }
    private Certification() { }
}
