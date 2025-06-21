using ContactFormApi.DTOs;

namespace ContactFormApi.Services;

public interface IEmailService
{
    Task SendContactEmailAsync(ContactFormDto contact);
}