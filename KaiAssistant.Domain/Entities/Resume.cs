namespace KaiAssistant.Domain.Entities;

public class Resume
{
    public string Summary { get; set; } = string.Empty;
    public SkillsSection? Skills { get; set; }
    public List<ExperienceItem>? Experience { get; set; }
    public List<ProjectItem>? Projects { get; set; }
}

public class SkillsSection
{
    public List<string>? LanguagesFrameworks { get; set; }
    public List<string>? Databases { get; set; }
    public List<string>? Frontend { get; set; }
    public List<string>? ArchitecturePatterns { get; set; }
    public List<string>? DevOps { get; set; }
    public List<string>? Security { get; set; }
    public List<string>? Testing { get; set; }
    public List<string>? Tools { get; set; }
    public List<string>? Other { get; set; }
}

public class ExperienceItem
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string>? Highlights { get; set; } 
}

public class ProjectItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}