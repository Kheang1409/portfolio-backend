using KaiAssistant.Domain.Entities.Resumes;

namespace KaiAssistant.Domain.Interfaces.Repositories;

public interface IResumeRepository
{
	Task<Resume?> GetByIdAsync(string id);
	Task<Resume?> GetLatestAsync();
	Task InsertAsync(Resume resume);
}

