using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduOS.Service.Services.Students;

public sealed class LearnerConsentService : ILearnerConsentService
{
    private readonly ILearnerConsentRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly LearnerIdentitySettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<LearnerConsentService> _logger;

    public LearnerConsentService(
        ILearnerConsentRepository repository,
        ICurrentUserService currentUser,
        IOptions<LearnerIdentitySettings> settings,
        TimeProvider clock,
        ILogger<LearnerConsentService> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<LearnerConsentRequestDto>>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanReview()) return Denied<IReadOnlyList<LearnerConsentRequestDto>>();

        try
        {
            var records = await _repository.GetPendingForUserAsync(
                _currentUser.UserId,
                UtcNow(),
                cancellationToken);
            IReadOnlyList<LearnerConsentRequestDto> data = records.Select(x => new LearnerConsentRequestDto
            {
                Reference = x.Reference,
                RequestingInstitution = x.RequestingInstitution,
                Purpose = x.Purpose,
                RequestedScopes = x.RequestedScopes,
                ExpiresAt = x.ExpiresAt
            }).ToList();
            return ApiResponse<IReadOnlyList<LearnerConsentRequestDto>>.SuccessResponse(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load learner consent requests for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<LearnerConsentRequestDto>>.ErrorResponse(
                "Consent requests could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<LearnerDataGrantDto>>> GetActiveGrantsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanReview()) return Denied<IReadOnlyList<LearnerDataGrantDto>>();

        try
        {
            var records = await _repository.GetActiveGrantsForUserAsync(
                _currentUser.UserId,
                UtcNow(),
                cancellationToken);
            IReadOnlyList<LearnerDataGrantDto> data = records.Select(x => new LearnerDataGrantDto
            {
                Reference = x.Reference,
                Institution = x.Institution,
                Purpose = x.Purpose,
                GrantedScopes = x.GrantedScopes,
                StartsAt = x.StartsAt,
                ExpiresAt = x.ExpiresAt
            }).ToList();
            return ApiResponse<IReadOnlyList<LearnerDataGrantDto>>.SuccessResponse(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load learner data grants for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<LearnerDataGrantDto>>.ErrorResponse(
                "Data grants could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<LearnerConsentResolutionDto>> ResolveAsync(
        Guid requestReference,
        ResolveLearnerConsentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanReview()) return Denied<LearnerConsentResolutionDto>();
        if (requestReference == Guid.Empty
            || request == null
            || !Enum.IsDefined(request.Decision))
        {
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "Consent decision is invalid.");
        }

        if (request.Decision == LearnerConsentDecision.Approve
            && _settings.DataGrantLifetimeDays is < 1 or > 3650)
        {
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "Learner consent is temporarily unavailable.", 503);
        }

        var now = UtcNow();
        try
        {
            var result = await _repository.ResolveAsync(
                requestReference,
                _currentUser.UserId,
                request.Decision,
                now,
                now.AddDays(_settings.DataGrantLifetimeDays),
                _currentUser.IpAddress,
                _currentUser.UserAgent,
                cancellationToken);
            return MapResolution(result);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent learner consent decision {Reference}", requestReference);
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "The consent request changed. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Learner consent decision failed for {Reference}", requestReference);
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "Consent could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<LearnerConsentResolutionDto>> RevokeAsync(
        Guid grantReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanReview()) return Denied<LearnerConsentResolutionDto>();
        if (grantReference == Guid.Empty)
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Data grant reference is invalid.");

        try
        {
            var result = await _repository.RevokeAsync(
                grantReference,
                _currentUser.UserId,
                UtcNow(),
                _currentUser.IpAddress,
                _currentUser.UserAgent,
                cancellationToken);
            return MapRevocation(result);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent learner data grant revocation {Reference}", grantReference);
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "The data grant changed. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Learner data grant revocation failed for {Reference}", grantReference);
            return ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
                "Data grant could not be revoked.", 500);
        }
    }

    private bool CanReview() =>
        _currentUser.IsAuthenticated
        && _currentUser.UserId > 0
        && (_currentUser.IsInRole("Student") || _currentUser.IsInRole("Parent"));

    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;

    private static ApiResponse<T> Denied<T>() =>
        ApiResponse<T>.ErrorResponse("Student or parent access is required.", 403);

    private static ApiResponse<LearnerConsentResolutionDto> MapResolution(
        LearnerConsentMutationResult result) => result.State switch
    {
        LearnerConsentMutationState.Approved => Success(
            "Approved", result, result.AlreadyProcessed
                ? "Consent was already approved."
                : "Consent approved."),
        LearnerConsentMutationState.Denied => Success(
            "Denied", result, result.AlreadyProcessed
                ? "Consent was already denied."
                : "Consent denied."),
        LearnerConsentMutationState.NotFound =>
            ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Consent request not found.", 404),
        LearnerConsentMutationState.Expired =>
            ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Consent request has expired.", 410),
        LearnerConsentMutationState.Revoked =>
            ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Consent has been revoked.", 409),
        _ => ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
            "Consent request conflicts with its current state.", 409)
    };

    private static ApiResponse<LearnerConsentResolutionDto> MapRevocation(
        LearnerConsentMutationResult result) => result.State switch
    {
        LearnerConsentMutationState.Revoked => Success(
            "Revoked", result, result.AlreadyProcessed
                ? "Data grant was already revoked."
                : "Data grant revoked."),
        LearnerConsentMutationState.NotFound =>
            ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Data grant not found.", 404),
        LearnerConsentMutationState.Expired =>
            ApiResponse<LearnerConsentResolutionDto>.ErrorResponse("Data grant has expired.", 410),
        _ => ApiResponse<LearnerConsentResolutionDto>.ErrorResponse(
            "Data grant conflicts with its current state.", 409)
    };

    private static ApiResponse<LearnerConsentResolutionDto> Success(
        string state,
        LearnerConsentMutationResult result,
        string message) => new()
    {
        Success = true,
        StatusCode = 200,
        Message = message,
        Data = new LearnerConsentResolutionDto
        {
            State = state,
            AlreadyProcessed = result.AlreadyProcessed,
            GrantReference = result.GrantReference,
            GrantExpiresAt = result.GrantExpiresAt
        }
    };
}
