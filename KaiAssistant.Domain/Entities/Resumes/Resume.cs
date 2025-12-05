using KaiAssistant.Domain.Entities.Certifications;
using KaiAssistant.Domain.Entities.Educations;
using KaiAssistant.Domain.Entities.Experiences;
using KaiAssistant.Domain.Entities.Projects;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace KaiAssistant.Domain.Entities.Resumes;
public class Resume
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public Personals? Personals { get; set; }
    public string Summary { get; set; } = string.Empty;

    private List<string> _skills = new();
    public IReadOnlyCollection<string> Skills => _skills.AsReadOnly();

    public void AddSkill(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
            return;

        if (!_skills.Contains(skill))
            _skills.Add(skill);
    }

    public bool RemoveSkill(string skill) => _skills.Remove(skill);

    private readonly HashSet<Experience> _experiences = new();
    public IReadOnlyCollection<Experience> Experiences => _experiences;

    private readonly HashSet<Project> _projects = new();
    public IReadOnlyCollection<Project> Projects => _projects;

    private readonly HashSet<Education> _educations = new();
    public IReadOnlyCollection<Education> Educations => _educations;

    private readonly HashSet<Certification> _certifications = new();
    public IReadOnlyCollection<Certification> Certifications => _certifications;

    public Resume() { }
}
