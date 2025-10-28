namespace KaiAssistant.Domain.Entities.Educations;

public class Education
{
    public string Degree { get; private set; } = string.Empty;
    public string Institution { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public DateTime? StartDates { get; private set; }
    public DateTime? EndDate { get; private set; }

    private Education() { }
}
