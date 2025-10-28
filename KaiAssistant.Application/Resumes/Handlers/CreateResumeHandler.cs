using KaiAssistant.Application.Resumes.Commands;
using KaiAssistant.Domain.Interfaces.Repositories;
using KaiAssistant.Domain.Entities.Resumes;
using MediatR;

namespace KaiAssistant.Application.Resumes.Handlers;

public class CreateResumeHandler : IRequestHandler<CreateResumeCommand, Resume>
{
    private readonly IResumeRepository _repository;

    public CreateResumeHandler(IResumeRepository repository)
    {
        _repository = repository;
    }

    public async Task<Resume> Handle(CreateResumeCommand request, CancellationToken cancellationToken)
    {
        await _repository.InsertAsync(request.Resume);
        return request.Resume;
    }
}
