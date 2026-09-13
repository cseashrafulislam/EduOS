using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Enums;

namespace EduOS.Core.Interfaces.IServices;

public interface ILearnerIdentityService
{
    Task<ApiResponse<LearnerIdentityResultDto>> RegisterOrRequestAsync(
        RegisterLearnerIdentityRequestDto request,
        CancellationToken cancellationToken = default);
}

public interface ILearnerIdentifierProtector
{
    bool TryNormalize(
        PersonIdentifierType type,
        string? suppliedValue,
        out string normalizedValue);

    string ComputeLookupDigest(PersonIdentifierType type, string normalizedValue);
    string Protect(string normalizedValue);
}
