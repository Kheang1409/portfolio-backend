/*
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities.Privacy;
using KaiAssistant.Infrastructure.Privacy;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace KaiAssistant.Tests
{
    class FakePolicyStore : IPrivacyPolicyStore
    {
        private readonly IEnumerable<PrivacyPolicy> _policies;
        public FakePolicyStore(IEnumerable<PrivacyPolicy> policies) => _policies = policies;
        public Task DeletePolicyAsync(string id, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<PrivacyPolicy>> GetPoliciesAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(_policies);
        public Task UpsertPolicyAsync(PrivacyPolicy policy, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class DynamicPrivacyServiceTests
    {
        [Theory]
        [InlineData("Contact me at john.doe@example.com","[REDACTED_EMAIL]")]
        [InlineData("My SSN is 123-45-6789","[REDACTED_SSN]")]
        [InlineData("Call +1 (555) 123-4567","[REDACTED_PHONE]")]
        [InlineData("Passport AB1234567","[REDACTED_PASSPORT]")]
        public async Task AppliesPolicies(string input, string expectedFragment)
        {
            var policies = new List<PrivacyPolicy>
            {
                new PrivacyPolicy { Name = "email", Pattern = "[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}", Replacement = "[REDACTED_EMAIL]", Enabled = true, Priority = 1 },
                new PrivacyPolicy { Name = "ssn", Pattern = "\\b\\d{3}-\\d{2}-\\d{4}\\b", Replacement = "[REDACTED_SSN]", Enabled = true, Priority = 2 },
                new PrivacyPolicy { Name = "phone", Pattern = "\\+?\\d[\\d\\s\\-()]{6,}\\d", Replacement = "[REDACTED_PHONE]", Enabled = true, Priority = 3 },
                new PrivacyPolicy { Name = "passport", Pattern = "\\b[A-Z]{1,2}\\d{6,9}\\b", Replacement = "[REDACTED_PASSPORT]", Enabled = true, Priority = 4 }
            };

            var store = new FakePolicyStore(policies);
            var svc = new DynamicPrivacyService(store, new Microsoft.Extensions.Logging.Abstractions.NullLogger<DynamicPrivacyService>());
            var outp = await Task.Run(() => svc.RedactPii(input));
            Assert.Contains(expectedFragment, outp);
        }
    }
}
*/
