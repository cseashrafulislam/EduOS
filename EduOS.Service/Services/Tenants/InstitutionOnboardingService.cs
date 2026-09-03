using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants
{
    public class InstitutionOnboardingService : IInstitutionOnboardingService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IGenericRepository<Tenant> _tenantRepo;
        private readonly IGenericRepository<Campus> _campusRepo;
        private readonly IGenericRepository<AcademicYear> _yearRepo;
        private readonly IGenericRepository<AcademicTerm> _termRepo;
        private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypeRepo;
        private readonly ITenantModuleService _tenantModuleService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<InstitutionOnboardingService> _logger;

        public InstitutionOnboardingService(
            UserManager<ApplicationUser> userManager,
            IGenericRepository<Tenant> tenantRepo,
            IGenericRepository<Campus> campusRepo,
            IGenericRepository<AcademicYear> yearRepo,
            IGenericRepository<AcademicTerm> termRepo,
            IGenericRepository<InstitutionTypeDefinition> institutionTypeRepo,
            ITenantModuleService tenantModuleService,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger<InstitutionOnboardingService> logger)
        {
            _userManager = userManager;
            _tenantRepo = tenantRepo;
            _campusRepo = campusRepo;
            _yearRepo = yearRepo;
            _termRepo = termRepo;
            _institutionTypeRepo = institutionTypeRepo;
            _tenantModuleService = tenantModuleService;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ============================================================
        // SIGNUP
        // ============================================================
        public async Task<ApiResponse<InstitutionSignupResponseDto>> RegisterInstitutionAsync(
            InstitutionSignupRequestDto dto, string baseUrl)
        {
            // Validate
            if (dto.Password != dto.ConfirmPassword)
                return Fail<InstitutionSignupResponseDto>("Passwords do not match");

            if (!dto.AgreeTerms)
                return Fail<InstitutionSignupResponseDto>("You must agree to the terms of service");

            InstitutionTypeDefinition? institutionType = null;
            if (!string.IsNullOrWhiteSpace(dto.InstitutionType))
            {
                if (!TryNormalizeCatalogCode(dto.InstitutionType, out var institutionTypeCode))
                    return Fail<InstitutionSignupResponseDto>("Select a valid institution type");

                institutionType = await _institutionTypeRepo.FirstOrDefaultAsync(x =>
                    x.Code == institutionTypeCode && x.IsActive && x.IsPubliclyVisible);
                if (institutionType == null)
                    return Fail<InstitutionSignupResponseDto>("Select a valid institution type");
            }

            // Check duplicate email
            var existing = await _userManager.FindByEmailAsync(dto.Email);
            if (existing != null)
                return Fail<InstitutionSignupResponseDto>(
                    "An account with this email already exists. Please login or use forgot password.");

            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await _unitOfWork.BeginTransactionAsync();

                try
                {

                    // 1. Create Tenant
                    var tenant = new Tenant
                    {
                        Name = dto.InstitutionName.Trim(),
                        Code = GenerateTenantCode(dto.InstitutionName),
                        Email = dto.Email.Trim().ToLower(),
                        OwnerName = dto.OwnerName.Trim(),
                        OwnerEmail = dto.Email.Trim().ToLower(),
                        OwnerPhone = dto.Phone?.Trim(),
                        InstitutionType = institutionType?.Code,
                        InstitutionTypeDefinitionId = institutionType?.Id,
                        Status = TenantStatus.PendingVerification,
                        OnboardingStep = OnboardingStep.EmailVerification,
                        IsOnboardingComplete = false,
                        IsEmailVerified = false,
                        IsActive = true,
                        Currency = "BDT",
                        CurrencySymbol = "৳",
                        TimeZone = "Asia/Dhaka",
                        Language = "en",
                        DateFormat = "dd-MM-yyyy",
                        PrimaryColor = "#1E40AF",
                        SecondaryColor = "#64748B",
                        AccentColor = "#F59E0B",
                        CreatedAt = DateTime.UtcNow
                    };

                    await _tenantRepo.AddAsync(tenant);
                    await _unitOfWork.SaveChangesAsync();

                    // 2. Create Application User
                    var user = new ApplicationUser
                    {
                        UserName = dto.Email.Trim().ToLower(),
                        Email = dto.Email.Trim().ToLower(),
                        FullName = dto.OwnerName.Trim(),
                        TenantId = tenant.Id,
                        UserType = "TenantAdmin",
                        IsActive = true,
                        EmailConfirmed = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    var createResult = await _userManager.CreateAsync(user, dto.Password);
                    if (!createResult.Succeeded)
                    {
                        await _unitOfWork.RollbackTransactionAsync();
                        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        _logger.LogWarning("Signup failed for {Email}: {Errors}", dto.Email, errors);
                        return Fail<InstitutionSignupResponseDto>(errors);
                    }

                    // Assign TenantAdmin role
                    await _userManager.AddToRoleAsync(user, "TenantAdmin");

                    // Update tenant with owner user ID
                    tenant.OwnerUserId = user.Id;
                    _tenantRepo.Update(tenant);
                    await _unitOfWork.SaveChangesAsync();

                    if (institutionType != null)
                    {
                        var presetResult = await _tenantModuleService.ApplyInstitutionPresetAsync(
                            tenant.Id,
                            institutionType.Id);
                        if (!presetResult.Succeeded)
                        {
                            await _unitOfWork.RollbackTransactionAsync();
                            return Fail<InstitutionSignupResponseDto>(presetResult.Message!);
                        }
                    }

                    await _unitOfWork.CommitTransactionAsync();

                    // 3. Send verification email (background job)
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    var verifyUrl = $"{baseUrl}/api/institution-onboarding/verify-email" +
                                    $"?email={Uri.EscapeDataString(user.Email!)}" +
                                    $"&token={Uri.EscapeDataString(token)}";

                    BackgroundJob.Enqueue<IEmailJob>(x =>
                        x.SendVerificationEmailAsync(user.Email!, dto.InstitutionName, user.FullName, verifyUrl));

                    _logger.LogInformation("Institution registered: {Name} ({Email})", dto.InstitutionName, dto.Email);

                    return Ok(new InstitutionSignupResponseDto
                    {
                        Success = true,
                        Message = "Account created! Please check your email to verify your account.",
                        Email = user.Email,
                        TenantId = tenant.Id,
                        UserId = user.Id
                    });
                }
                catch (Exception ex)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    _logger.LogError(ex, "Signup failed for {Email}", dto.Email);
                    return Fail<InstitutionSignupResponseDto>("Registration failed. Please try again.");
                }
            });
        }

        // ============================================================
        // EMAIL VERIFY
        // ============================================================
        public async Task<bool> VerifyEmailAsync(string email, string token, string baseUrl)
        {
            try
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null) return false;

                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Email verify failed for {Email}", email);
                    return false;
                }

                // Update tenant
                if (user.TenantId.HasValue)
                {
                    var tenant = await _tenantRepo.GetByIdAsync(user.TenantId.Value);
                    if (tenant != null)
                    {
                        tenant.IsEmailVerified = true;
                        tenant.EmailVerifiedAt = DateTime.UtcNow;
                        tenant.Status = TenantStatus.Onboarding;
                        tenant.OnboardingStep = OnboardingStep.InstitutionProfile;
                        _tenantRepo.Update(tenant);
                        await _unitOfWork.SaveChangesAsync();
                    }
                }

                _logger.LogInformation("Email verified: {Email}", email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email verify error for {Email}", email);
                return false;
            }
        }

        // ============================================================
        // INSTITUTION PROFILE
        // ============================================================
        public async Task<ApiResponse<InstitutionProfileSetupDto?>> GetInstitutionProfileAsync()
        {
            try
            {
                var tenant = await GetCurrentTenantAsync();
                if (tenant == null)
                    return Fail<InstitutionProfileSetupDto?>("Tenant not found");

                return Ok<InstitutionProfileSetupDto?>(new InstitutionProfileSetupDto
                {
                    InstitutionName = tenant.Name,
                    InstitutionType = tenant.InstitutionType,
                    OwnerName = tenant.OwnerName,
                    OwnerPhone = tenant.OwnerPhone,
                    OwnerEmail = tenant.OwnerEmail,
                    OwnerDesignation = tenant.OwnerDesignation,
                    Phone = tenant.Phone,
                    Website = tenant.Website,
                    Address = tenant.Address,
                    City = tenant.City,
                    State = tenant.State,
                    Country = tenant.Country,
                    PostalCode = tenant.PostalCode,
                    Email = tenant.Email
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInstitutionProfile error");
                return Fail<InstitutionProfileSetupDto?>("Failed to load profile");
            }
        }

        public async Task<ApiResponse<bool>> SaveInstitutionProfileAsync(InstitutionProfileSetupDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.InstitutionName))
                    return Fail<bool>("Institution name is required");

                if (string.IsNullOrWhiteSpace(dto.OwnerName))
                    return Fail<bool>("Owner name is required");

                if (!TryNormalizeCatalogCode(dto.InstitutionType, out var institutionTypeCode))
                    return Fail<bool>("Institution type is required");

                var institutionType = await _institutionTypeRepo.FirstOrDefaultAsync(x =>
                    x.Code == institutionTypeCode && x.IsActive && x.IsPubliclyVisible);
                if (institutionType == null)
                    return Fail<bool>("Select a valid institution type");

                var tenant = await GetCurrentTenantAsync();
                if (tenant == null)
                    return Fail<bool>("Tenant not found", 404);

                tenant.Name = dto.InstitutionName.Trim();
                tenant.InstitutionType = institutionType.Code;
                tenant.InstitutionTypeDefinitionId = institutionType.Id;
                tenant.OwnerName = dto.OwnerName.Trim();
                tenant.OwnerPhone = dto.OwnerPhone?.Trim();
                tenant.OwnerEmail = dto.OwnerEmail?.Trim();
                tenant.OwnerDesignation = dto.OwnerDesignation?.Trim();
                tenant.Phone = dto.Phone?.Trim();
                tenant.Website = dto.Website?.Trim();
                tenant.Address = dto.Address?.Trim();
                tenant.City = dto.City?.Trim();
                tenant.State = dto.State?.Trim();
                tenant.Country = dto.Country?.Trim() ?? "Bangladesh";
                tenant.PostalCode = dto.PostalCode?.Trim();
                tenant.UpdatedAt = DateTime.UtcNow;

                _tenantRepo.Update(tenant);
                var presetResult = await _tenantModuleService.ApplyInstitutionPresetAsync(
                    tenant.Id,
                    institutionType.Id);
                if (!presetResult.Succeeded)
                    return Fail<bool>(presetResult.Message!);

                return Ok(true, "Profile saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveInstitutionProfile error");
                return Fail<bool>("Failed to save profile");
            }
        }

        // ============================================================
        // CAMPUS
        // ============================================================
        public async Task<ApiResponse<List<CampusListItemDto>>> GetCampusListAsync()
        {
            try
            {
                var campuses = await _campusRepo.GetQueryable()
                    .Where(c => c.TenantId == _currentUser.TenantId)
                    .OrderByDescending(c => c.IsHeadOffice)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                var dtos = campuses.Select(c => new CampusListItemDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    Address = c.Address,
                    Phone = c.Phone,
                    HeadName = c.HeadName,
                    IsHeadOffice = c.IsHeadOffice,
                    IsActive = c.IsActive
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCampusList error");
                return Fail<List<CampusListItemDto>>("Failed to load campuses");
            }
        }

        public async Task<ApiResponse<CampusSetupDto?>> GetCampusByIdAsync(long id)
        {
            try
            {
                var c = await _campusRepo.GetByIdAsync(id);
                if (c == null || c.TenantId != _currentUser.TenantId)
                    return Fail<CampusSetupDto?>("Campus not found", 404);

                return Ok<CampusSetupDto?>(new CampusSetupDto
                {
                    Id = c.Id, Name = c.Name, Code = c.Code,
                    Address = c.Address, Phone = c.Phone,
                    Email = c.Email, HeadName = c.HeadName,
                    IsHeadOffice = c.IsHeadOffice
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCampusById error");
                return Fail<CampusSetupDto?>("Failed to load campus");
            }
        }

        public async Task<ApiResponse<bool>> SaveCampusAsync(CampusSetupDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return Fail<bool>("Campus name is required");

                var tenant = await GetCurrentTenantAsync();
                if (tenant == null)
                    return Fail<bool>("Tenant not found", 404);

                var isNewCampus = !dto.Id.HasValue || dto.Id <= 0;
                var currentCampusCount = await _campusRepo.GetQueryable()
                    .CountAsync(c => c.TenantId == _currentUser.TenantId);

                if (isNewCampus && tenant.MaxCampuses > 0
                    && currentCampusCount >= tenant.MaxCampuses)
                {
                    return Fail<bool>("Campus limit reached for the active plan", 409);
                }

                var normalizedCode = string.IsNullOrWhiteSpace(dto.Code)
                    ? null
                    : dto.Code.Trim().ToUpperInvariant();
                if (normalizedCode != null)
                {
                    var codeInUse = await _campusRepo.GetQueryable().AnyAsync(c =>
                        c.TenantId == _currentUser.TenantId
                        && c.Id != (dto.Id ?? 0)
                        && c.Code != null
                        && c.Code.ToUpper() == normalizedCode);
                    if (codeInUse)
                        return Fail<bool>("Campus code is already in use", 409);
                }

                var shouldBeHeadOffice = dto.IsHeadOffice || (isNewCampus && currentCampusCount == 0);

                // If marking as head office, unmark others
                if (shouldBeHeadOffice)
                {
                    var others = await _campusRepo.GetQueryable()
                        .Where(c => c.TenantId == _currentUser.TenantId
                                 && c.IsHeadOffice
                                 && c.Id != (dto.Id ?? 0))
                        .ToListAsync();

                    foreach (var o in others)
                    {
                        o.IsHeadOffice = false;
                        _campusRepo.Update(o);
                    }
                }

                if (dto.Id.HasValue && dto.Id > 0)
                {
                    // Update
                    var campus = await _campusRepo.GetByIdAsync(dto.Id.Value);
                    if (campus == null || campus.TenantId != _currentUser.TenantId)
                        return Fail<bool>("Campus not found", 404);

                    campus.Name = dto.Name.Trim();
                    campus.Code = normalizedCode ?? string.Empty;
                    campus.Address = dto.Address?.Trim();
                    campus.Phone = dto.Phone?.Trim();
                    campus.Email = dto.Email?.Trim();
                    campus.HeadName = dto.HeadName?.Trim();
                    campus.IsHeadOffice = shouldBeHeadOffice;
                    campus.UpdatedAt = DateTime.UtcNow;
                    campus.UpdatedBy = _currentUser.UserId;
                    _campusRepo.Update(campus);
                }
                else
                {
                    // Create
                    var campus = new Campus
                    {
                        TenantId = _currentUser.TenantId,
                        Name = dto.Name.Trim(),
                        Code = normalizedCode ?? string.Empty,
                        Address = dto.Address?.Trim(),
                        Phone = dto.Phone?.Trim(),
                        Email = dto.Email?.Trim(),
                        HeadName = dto.HeadName?.Trim(),
                        IsHeadOffice = shouldBeHeadOffice,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUser.UserId
                    };
                    await _campusRepo.AddAsync(campus);
                }

                await _unitOfWork.SaveChangesAsync();
                return Ok(true, dto.Id.HasValue ? "Campus updated" : "Campus added");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveCampus error");
                return Fail<bool>("Failed to save campus");
            }
        }

        public async Task<ApiResponse<bool>> DeleteCampusAsync(long id)
        {
            try
            {
                var campus = await _campusRepo.GetByIdAsync(id);
                if (campus == null || campus.TenantId != _currentUser.TenantId)
                    return Fail<bool>("Campus not found", 404);

                campus.IsDeleted = true;
                campus.UpdatedAt = DateTime.UtcNow;
                campus.UpdatedBy = _currentUser.UserId;
                _campusRepo.Update(campus);

                if (campus.IsHeadOffice)
                {
                    var replacement = await _campusRepo.GetQueryable()
                        .Where(c => c.TenantId == _currentUser.TenantId && c.Id != id)
                        .OrderBy(c => c.Id)
                        .FirstOrDefaultAsync();
                    if (replacement != null)
                    {
                        replacement.IsHeadOffice = true;
                        replacement.UpdatedAt = DateTime.UtcNow;
                        replacement.UpdatedBy = _currentUser.UserId;
                        _campusRepo.Update(replacement);
                    }
                }

                await _unitOfWork.SaveChangesAsync();

                return Ok(true, "Campus deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteCampus error");
                return Fail<bool>("Failed to delete campus");
            }
        }

        // ============================================================
        // ACADEMIC YEAR
        // ============================================================
        public async Task<ApiResponse<List<AcademicYearListItemDto>>> GetAcademicYearListAsync()
        {
            try
            {
                var years = await _yearRepo.GetQueryable()
                    .Where(y => y.TenantId == _currentUser.TenantId)
                    .OrderByDescending(y => y.IsCurrent)
                    .ThenByDescending(y => y.StartDate)
                    .ToListAsync();

                return Ok(years.Select(y => new AcademicYearListItemDto
                {
                    Id = y.Id, Name = y.Name,
                    StartDate = y.StartDate, EndDate = y.EndDate,
                    IsCurrent = y.IsCurrent
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAcademicYearList error");
                return Fail<List<AcademicYearListItemDto>>("Failed to load academic years");
            }
        }

        public async Task<ApiResponse<AcademicYearSetupDto?>> GetAcademicYearByIdAsync(long id)
        {
            try
            {
                var y = await _yearRepo.GetByIdAsync(id);
                if (y == null || y.TenantId != _currentUser.TenantId)
                    return Fail<AcademicYearSetupDto?>("Academic year not found", 404);

                return Ok<AcademicYearSetupDto?>(new AcademicYearSetupDto
                {
                    Id = y.Id, Name = y.Name,
                    StartDate = y.StartDate, EndDate = y.EndDate,
                    IsCurrent = y.IsCurrent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAcademicYearById error");
                return Fail<AcademicYearSetupDto?>("Failed to load academic year");
            }
        }

        public async Task<ApiResponse<bool>> SaveAcademicYearAsync(AcademicYearSetupDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return Fail<bool>("Academic year name is required");

                if (dto.StartDate >= dto.EndDate)
                    return Fail<bool>("End date must be after start date");

                var normalizedName = dto.Name.Trim();
                var nameInUse = await _yearRepo.GetQueryable().AnyAsync(y =>
                    y.TenantId == _currentUser.TenantId
                    && y.Id != (dto.Id ?? 0)
                    && y.Name.ToUpper() == normalizedName.ToUpper());
                if (nameInUse)
                    return Fail<bool>("Academic year name is already in use", 409);

                if (dto.Id.HasValue && dto.Id > 0)
                {
                    var hasTermOutsideRange = await _termRepo.GetQueryable().AnyAsync(t =>
                        t.AcademicYearId == dto.Id.Value
                        && ((t.StartDate.HasValue && t.StartDate.Value < dto.StartDate)
                            || (t.EndDate.HasValue && t.EndDate.Value > dto.EndDate)));
                    if (hasTermOutsideRange)
                        return Fail<bool>("Academic year dates must include all existing terms", 409);
                }

                // If marking as current, unmark others
                if (dto.IsCurrent)
                {
                    var others = await _yearRepo.GetQueryable()
                        .Where(y => y.TenantId == _currentUser.TenantId
                                 && y.IsCurrent && y.Id != (dto.Id ?? 0))
                        .ToListAsync();

                    foreach (var o in others) { o.IsCurrent = false; _yearRepo.Update(o); }
                }

                if (dto.Id.HasValue && dto.Id > 0)
                {
                    var year = await _yearRepo.GetByIdAsync(dto.Id.Value);
                    if (year == null || year.TenantId != _currentUser.TenantId)
                        return Fail<bool>("Academic year not found", 404);

                    year.Name = normalizedName;
                    year.StartDate = dto.StartDate;
                    year.EndDate = dto.EndDate;
                    year.IsCurrent = dto.IsCurrent;
                    year.UpdatedAt = DateTime.UtcNow;
                    year.UpdatedBy = _currentUser.UserId;
                    _yearRepo.Update(year);
                }
                else
                {
                    await _yearRepo.AddAsync(new AcademicYear
                    {
                        TenantId = _currentUser.TenantId,
                        Name = normalizedName,
                        StartDate = dto.StartDate,
                        EndDate = dto.EndDate,
                        IsCurrent = dto.IsCurrent,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUser.UserId
                    });
                }

                await _unitOfWork.SaveChangesAsync();
                return Ok(true, dto.Id.HasValue ? "Academic year updated" : "Academic year added");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveAcademicYear error");
                return Fail<bool>("Failed to save academic year");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAcademicYearAsync(long id)
        {
            try
            {
                var year = await _yearRepo.GetByIdAsync(id);
                if (year == null || year.TenantId != _currentUser.TenantId)
                    return Fail<bool>("Academic year not found", 404);

                // Check if any terms exist
                var hasTerms = await _termRepo.GetQueryable()
                    .AnyAsync(t => t.AcademicYearId == id);

                if (hasTerms)
                    return Fail<bool>("Remove all terms first before deleting this year");

                year.IsDeleted = true;
                year.UpdatedAt = DateTime.UtcNow;
                _yearRepo.Update(year);
                await _unitOfWork.SaveChangesAsync();
                return Ok(true, "Academic year deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteAcademicYear error");
                return Fail<bool>("Failed to delete academic year");
            }
        }

        // ============================================================
        // ACADEMIC TERM
        // ============================================================
        public async Task<ApiResponse<List<AcademicTermListItemDto>>> GetAcademicTermListAsync()
        {
            try
            {
                var terms = await _termRepo.GetQueryable()
                    .Include(t => t.AcademicYear)
                    .Where(t => t.TenantId == _currentUser.TenantId)
                    .OrderBy(t => t.AcademicYearId)
                    .ThenBy(t => t.StartDate)
                    .ToListAsync();

                return Ok(terms.Select(t => new AcademicTermListItemDto
                {
                    Id = t.Id,
                    AcademicYearId = t.AcademicYearId,
                    AcademicYearName = t.AcademicYear?.Name ?? string.Empty,
                    Name = t.Name,
                    StartDate = t.StartDate,
                    EndDate = t.EndDate
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAcademicTermList error");
                return Fail<List<AcademicTermListItemDto>>("Failed to load terms");
            }
        }

        public async Task<ApiResponse<AcademicTermSetupDto?>> GetAcademicTermByIdAsync(long id)
        {
            try
            {
                var t = await _termRepo.GetByIdAsync(id);
                if (t == null || t.TenantId != _currentUser.TenantId)
                    return Fail<AcademicTermSetupDto?>("Term not found", 404);

                return Ok<AcademicTermSetupDto?>(new AcademicTermSetupDto
                {
                    Id = t.Id, AcademicYearId = t.AcademicYearId,
                    Name = t.Name, StartDate = t.StartDate, EndDate = t.EndDate
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAcademicTermById error");
                return Fail<AcademicTermSetupDto?>("Failed to load term");
            }
        }

        public async Task<ApiResponse<bool>> SaveAcademicTermAsync(AcademicTermSetupDto dto)
        {
            try
            {
                if (dto.AcademicYearId <= 0)
                    return Fail<bool>("Academic year is required");

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return Fail<bool>("Term name is required");

                if (dto.StartDate.HasValue != dto.EndDate.HasValue)
                    return Fail<bool>("Provide both term dates or leave both empty");

                if (dto.StartDate.HasValue && dto.StartDate.Value >= dto.EndDate!.Value)
                    return Fail<bool>("Term end date must be after start date");

                // Validate academic year belongs to this tenant
                var year = await _yearRepo.GetByIdAsync(dto.AcademicYearId);
                if (year == null || year.TenantId != _currentUser.TenantId)
                    return Fail<bool>("Academic year not found", 404);

                if (dto.StartDate.HasValue
                    && (dto.StartDate.Value < year.StartDate || dto.EndDate!.Value > year.EndDate))
                {
                    return Fail<bool>("Term dates must be within the academic year", 409);
                }

                var normalizedName = dto.Name.Trim();
                var nameInUse = await _termRepo.GetQueryable().AnyAsync(t =>
                    t.AcademicYearId == dto.AcademicYearId
                    && t.Id != (dto.Id ?? 0)
                    && t.Name.ToUpper() == normalizedName.ToUpper());
                if (nameInUse)
                    return Fail<bool>("Term name is already in use for this academic year", 409);

                if (dto.Id.HasValue && dto.Id > 0)
                {
                    var term = await _termRepo.GetByIdAsync(dto.Id.Value);
                    if (term == null || term.TenantId != _currentUser.TenantId)
                        return Fail<bool>("Term not found", 404);

                    term.Name = normalizedName;
                    term.AcademicYearId = dto.AcademicYearId;
                    term.StartDate = dto.StartDate;
                    term.EndDate = dto.EndDate;
                    term.UpdatedAt = DateTime.UtcNow;
                    term.UpdatedBy = _currentUser.UserId;
                    _termRepo.Update(term);
                }
                else
                {
                    await _termRepo.AddAsync(new AcademicTerm
                    {
                        TenantId = _currentUser.TenantId,
                        AcademicYearId = dto.AcademicYearId,
                        Name = normalizedName,
                        StartDate = dto.StartDate,
                        EndDate = dto.EndDate,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUser.UserId
                    });
                }

                await _unitOfWork.SaveChangesAsync();
                return Ok(true, dto.Id.HasValue ? "Term updated" : "Term added");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveAcademicTerm error");
                return Fail<bool>("Failed to save term");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAcademicTermAsync(long id)
        {
            try
            {
                var term = await _termRepo.GetByIdAsync(id);
                if (term == null || term.TenantId != _currentUser.TenantId)
                    return Fail<bool>("Term not found", 404);

                term.IsDeleted = true;
                term.UpdatedAt = DateTime.UtcNow;
                _termRepo.Update(term);
                await _unitOfWork.SaveChangesAsync();
                return Ok(true, "Term deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteAcademicTerm error");
                return Fail<bool>("Failed to delete term");
            }
        }

        // ============================================================
        // FINAL COMPLETE
        // ============================================================
        public async Task<ApiResponse<bool>> FinalCompleteAsync()
        {
            try
            {
                var tenant = await GetCurrentTenantAsync();
                if (tenant == null)
                    return Fail<bool>("Tenant not found", 404);

                // Validate minimum requirements
                if (!tenant.InstitutionTypeDefinitionId.HasValue)
                    return Fail<bool>("Please select a valid institution type before completing setup");

                var hasCampus = await _campusRepo.GetQueryable()
                    .AnyAsync(c => c.TenantId == _currentUser.TenantId);

                if (!hasCampus)
                    return Fail<bool>("Please add at least one campus before completing setup");

                var hasYear = await _yearRepo.GetQueryable()
                    .AnyAsync(y => y.TenantId == _currentUser.TenantId);

                if (!hasYear)
                    return Fail<bool>("Please add at least one academic year before completing setup");

                tenant.IsOnboardingComplete = true;
                tenant.OnboardingStep = OnboardingStep.Completed;
                tenant.OnboardingCompletedAt = DateTime.UtcNow;
                if (tenant.Status == TenantStatus.Onboarding)
                    tenant.Status = TenantStatus.Active;

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Onboarding complete for tenant {TenantId}", _currentUser.TenantId);
                return Ok(true, "Onboarding complete! Welcome to EduOS.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FinalComplete error");
                return Fail<bool>("Failed to complete onboarding");
            }
        }

        // ============================================================
        // PRIVATE HELPERS
        // ============================================================
        private async Task<Tenant?> GetCurrentTenantAsync()
            => await _tenantRepo.GetByIdAsync(_currentUser.TenantId);

        private static string GenerateTenantCode(string name)
        {
            var clean = new string(name.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant();
            return $"{clean}-{DateTime.UtcNow:yyMMdd}";
        }

        private static ApiResponse<T> Ok<T>(T data, string message = "Success")
            => ApiResponse<T>.SuccessResponse(data, message);

        private static ApiResponse<T> Fail<T>(string message, int statusCode = 400)
            => ApiResponse<T>.ErrorResponse(message, statusCode);

        private static bool TryNormalizeCatalogCode(string? code, out string normalizedCode)
        {
            normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
            return normalizedCode.Length is > 0 and <= 50
                   && normalizedCode.All(character =>
                       char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
        }
    }
}
