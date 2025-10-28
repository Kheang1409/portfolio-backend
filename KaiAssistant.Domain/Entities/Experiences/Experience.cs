namespace KaiAssistant.Domain.Entities.Experiences;

public class Experience
{
    public string Role { get; private set; } = string.Empty;
    public string Company { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public DateTime? StartDates { get; private set; }
    public DateTime? EndDate { get; private set; }
    private HashSet<string> _bulletPoints { get; set; } = new();
    public IReadOnlyCollection<string> BulletPoints => _bulletPoints;

    private Experience() { }
}
