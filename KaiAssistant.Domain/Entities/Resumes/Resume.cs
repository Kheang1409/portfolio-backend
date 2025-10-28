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

    public List<string>? Skills { get; set; }

    public HashSet<Experience> Experiences { get; set; } = new();

    public HashSet<Project> Projects { get; set; } = new();

    public HashSet<Education> Educations { get; set; } = new();

    public HashSet<Certification> Certifications { get; set; } = new();

    public Resume() { }
}
