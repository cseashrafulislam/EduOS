//using EduOS.Core.DTOs.SaaS;
//using EduOS.Core.Entities.Auth;
//using EduOS.Core.Entities.SaaS;
//using EduOS.Core.Enums.Interfaces.Auth;
//using EduOS.Core.Helpers;
//using EduOS.Core.Interfaces;
//using EduOS.Core.Interfaces.Jobs;
//using EduOS.Core.Interfaces.SaaS;
//using Hangfire;
//using Microsoft.AspNetCore.Hosting;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.Extensions.Hosting;

//namespace EduOS.Service.Services.SaaS
//{
//    public class InstitutionOnboardingService : IInstitutionOnboardingService
//    {
//        private readonly IUnitOfWork _unitOfWork;
//        private readonly UserManager<ApplicationUser> _userManager;
//        private readonly RoleManager<ApplicationRole> _roleManager;
//        private readonly IEmailService _emailService;
//        private readonly IWebHostEnvironment _environment;
//        public InstitutionOnboardingService(
//            IUnitOfWork unitOfWork,
//            UserManager<ApplicationUser> userManager,
//            RoleManager<ApplicationRole> roleManager,
//            IWebHostEnvironment environment)
//        {
//            _unitOfWork = unitOfWork;
//            _userManager = userManager;
//            _roleManager = roleManager;
//            _environment = environment;
//        }

//        public async Task<InstitutionSignupResponseDto> RegisterInstitutionAsync(InstitutionSignupRequestDto dto, string baseUrl)
//        {
//            await _unitOfWork.BeginTransactionAsync();

//            try
//            {
//                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
//                if (existingUser != null)
//                {
//                    return new InstitutionSignupResponseDto
//                    {
//                        Success = false,
//                        Message = "Email already exists."
//                    };
//                }

//                var existingTenant = await _unitOfWork.Tenants
//                    .FirstOrDefaultAsync(x => x.Email == dto.Email || x.Name == dto.InstitutionName);

//                if (existingTenant != null)
//                {
//                    return new InstitutionSignupResponseDto
//                    {
//                        Success = false,
//                        Message = "Institution already exists."
//                    };
//                }

//                var tenant = new Tenant
//                {
//                    Name = dto.InstitutionName,
//                    Code = GenerateTenantCode(dto.InstitutionName),
//                    InstitutionType = dto.InstitutionType,
//                    OwnerName = dto.OwnerName,
//                    Email = dto.Email,
//                    Phone = dto.Phone,
//                    Address = dto.Address,
//                    IsActive = false,
//                    IsEmailVerified = false,
//                    IsSetupCompleted = false
//                };

//                await _unitOfWork.Tenants.AddAsync(tenant);
//                await _unitOfWork.SaveChangesAsync();

//                var user = new ApplicationUser
//                {
//                    FullName = dto.OwnerName,
//                    UserName = dto.Email,
//                    Email = dto.Email,
//                    PhoneNumber = dto.Phone,
//                    EmailConfirmed = false
//                };

//                var userResult = await _userManager.CreateAsync(user, dto.Password);
//                if (!userResult.Succeeded)
//                {
//                    await _unitOfWork.RollbackTransactionAsync();

//                    return new InstitutionSignupResponseDto
//                    {
//                        Success = false,
//                        Message = string.Join(", ", userResult.Errors.Select(x => x.Description))
//                    };
//                }

//                if (!await _roleManager.RoleExistsAsync("TenantAdmin"))
//                {
//                    await _roleManager.CreateAsync(new ApplicationRole
//                    {
//                        Name = "TenantAdmin",
//                        NormalizedName = "TENANTADMIN"
//                    });
//                }

//                var addRoleResult = await _userManager.AddToRoleAsync(user, "TenantAdmin");
//                if (!addRoleResult.Succeeded)
//                {
//                    await _unitOfWork.RollbackTransactionAsync();

//                    return new InstitutionSignupResponseDto
//                    {
//                        Success = false,
//                        Message = "Failed to assign TenantAdmin role."
//                    };
//                }

//                await _unitOfWork.TenantUsers.AddAsync(new TenantUser
//                {
//                    TenantId = tenant.Id,
//                    UserId = user.Id,
//                    IsOwner = true,
//                    IsActive = true
//                });

//                var plan = await _unitOfWork.SubscriptionPlans
//                    .FirstOrDefaultAsync(x => x.IsActive && x.IsTrialAvailable);

//                if (plan == null)
//                {
//                    await _unitOfWork.RollbackTransactionAsync();

//                    return new InstitutionSignupResponseDto
//                    {
//                        Success = false,
//                        Message = "No active trial plan found."
//                    };
//                }

//                await _unitOfWork.TenantSubscriptions.AddAsync(new TenantSubscription
//                {
//                    TenantId = tenant.Id,
//                    SubscriptionPlanId = (int)plan.Id,
//                    FixedAmount = plan.FixedAmount,
//                    PerActiveStudentAmount = plan.PerActiveStudentAmount,
//                    StartDate = DateTime.UtcNow,
//                    EndDate = DateTime.UtcNow.AddDays(plan.TrialDays),
//                    IsTrial = true,
//                    IsActive = true
//                });

//                var planFeatures = await _unitOfWork.PlanFeatures
//                    .FindAsync(x => x.SubscriptionPlanId == plan.Id && x.IsEnabled);

//                foreach (var item in planFeatures)
//                {
//                    await _unitOfWork.TenantFeatures.AddAsync(new TenantFeature
//                    {
//                        TenantId = tenant.Id,
//                        FeatureId = item.FeatureId,
//                        IsEnabled = true
//                    });
//                }

//                var token = Guid.NewGuid().ToString("N");

//                await _unitOfWork.EmailVerificationTokens.AddAsync(new EmailVerificationToken
//                {
//                    UserId = user.Id,
//                    Email = user.Email!,
//                    Token = token,
//                    ExpireAt = DateTime.UtcNow.AddHours(24),
//                    IsUsed = false
//                });

//                await _unitOfWork.OnboardingProgresses.AddAsync(new OnboardingProgress
//                {
//                    TenantId = tenant.Id,
//                    AccountCreated = true,
//                    EmailVerified = false,
//                    InstitutionProfileCompleted = false,
//                    CampusSetupCompleted = false,
//                    AcademicSetupCompleted = false,
//                    AdminUserSetupCompleted = false,
//                    RolePermissionSetupCompleted = false,
//                    SubscriptionSetupCompleted = true,
//                    FinalCompleted = false,
//                    CurrentStep = 1
//                });

//                await _unitOfWork.SaveChangesAsync();

//                var verifyUrl = $"{baseUrl}/api/institution-onboarding/verify-email?email={Uri.EscapeDataString(user.Email!)}&token={token}";

//                BackgroundJob.Enqueue<IEmailJob>(x =>
//                    x.SendVerificationEmailAsync(
//                        user.Email!,
//                        tenant.Name,
//                        dto.OwnerName,
//                        verifyUrl
//                    ));

//                await _unitOfWork.CommitTransactionAsync();

//                return new InstitutionSignupResponseDto
//                {
//                    Success = true,
//                    Message = "Institution signup successful. Please verify your email.",
//                    TenantId = tenant.Id,
//                    UserId = user.Id
//                };
//            }
//            catch
//            {
//                await _unitOfWork.RollbackTransactionAsync();
//                throw;
//            }
//        }

//        public async Task<bool> VerifyEmailAsync(string email, string token, string baseUrl)
//        {
//            var user = await _userManager.FindByEmailAsync(email);
//            if (user == null) return false;

//            var savedToken = await _unitOfWork.EmailVerificationTokens.FirstOrDefaultAsync(x =>
//                x.Email == email &&
//                x.Token == token &&
//                !x.IsUsed &&
//                x.ExpireAt > DateTime.UtcNow);

//            if (savedToken == null) return false;

//            user.EmailConfirmed = true;
//            var updateResult = await _userManager.UpdateAsync(user);
//            if (!updateResult.Succeeded) return false;

//            savedToken.IsUsed = true;
//            savedToken.VerifiedAt = DateTime.UtcNow;

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Email == email);
//            if (tenant != null)
//            {
//                tenant.IsEmailVerified = true;
//                tenant.IsActive = true;
//            }

//            if (tenant != null)
//            {
//                var onboarding = await _unitOfWork.OnboardingProgresses
//                    .FirstOrDefaultAsync(x => x.TenantId == tenant.Id);

//                if (onboarding != null)
//                {
//                    onboarding.EmailVerified = true;
//                    onboarding.CurrentStep = 2;
//                }
//            }

//            await _unitOfWork.SaveChangesAsync();

//            var loginUrl = $"{baseUrl}/Account/Login";
//            var setPasswordUrl = $"{baseUrl}/Account/ForgotPassword";

//            BackgroundJob.Enqueue<IEmailJob>(x =>
//                x.SendVerificationSuccessEmailAsync(
//                    user.Email!,
//                    tenant != null ? tenant.Name : "EduOS Institution",
//                    user.FullName,
//                    user.Email!,
//                    loginUrl,
//                    setPasswordUrl
//                ));

//            return true;
//        }

//        private string GenerateTenantCode(string institutionName)
//        {
//            var chars = institutionName
//                .ToUpper()
//                .Where(char.IsLetterOrDigit)
//                .Take(6)
//                .ToArray();

//            var prefix = new string(chars);
//            return $"{prefix}{DateTime.UtcNow:ddHHmm}";
//        }


//        public async Task<InstitutionProfileSetupDto?> GetInstitutionProfileAsync()
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return null;

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value);
//            if (tenant == null) return null;

//            return new InstitutionProfileSetupDto
//            {
//                InstitutionName = tenant.Name,
//                InstitutionType = tenant.InstitutionType,
//                OwnerName = tenant.OwnerName,

//                Email = tenant.Email,
//                Phone = tenant.Phone,
//                AlternatePhone = tenant.AlternatePhone,
//                Address = tenant.Address,

//                ContactPersonName = tenant.ContactPersonName,
//                ContactPersonDesignation = tenant.ContactPersonDesignation,
//                ContactPersonEmail = tenant.ContactPersonEmail,

//                ShortName = tenant.ShortName,
//                TimeZone = string.IsNullOrWhiteSpace(tenant.TimeZone) ? "Asia/Dhaka" : tenant.TimeZone,
//                Currency = string.IsNullOrWhiteSpace(tenant.Currency) ? "BDT" : tenant.Currency,

//                Country = tenant.Country,
//                Division = tenant.Division,
//                District = tenant.District,
//                Thana = tenant.Thana,
//                PostCode = tenant.PostCode,

//                Subdomain = tenant.Subdomain,
//                CustomDomain = tenant.CustomDomain,

//                LogoUrl = tenant.LogoUrl,
//                FaviconUrl = tenant.FaviconUrl,

//                PrimaryColor = tenant.PrimaryColor,
//                SecondaryColor = tenant.SecondaryColor,
//                WebsiteUrl = tenant.WebsiteUrl,

//                EIIN = tenant.EIIN,
//                RegistrationNumber = tenant.RegistrationNumber,
//                EducationBoard = tenant.EducationBoard,
//                EstablishedDate = tenant.EstablishedDate,

//                InstitutionCode = tenant.InstitutionCode,

//                Language = string.IsNullOrWhiteSpace(tenant.Language) ? "en" : tenant.Language,
//                DateFormat = string.IsNullOrWhiteSpace(tenant.DateFormat) ? "dd-MMM-yyyy" : tenant.DateFormat
//            };
//        }

//        public async Task<bool> SaveInstitutionProfileAsync(InstitutionProfileSetupDto dto)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value);
//            if (tenant == null) return false;

//            tenant.Name = dto.InstitutionName?.Trim() ?? string.Empty;
//            tenant.InstitutionType = dto.InstitutionType?.Trim() ?? string.Empty;
//            tenant.OwnerName = dto.OwnerName?.Trim() ?? string.Empty;

//            tenant.Email = dto.Email?.Trim() ?? string.Empty;
//            tenant.Phone = NormalizeNullable(dto.Phone);
//            tenant.AlternatePhone = NormalizeNullable(dto.AlternatePhone);
//            tenant.Address = NormalizeNullable(dto.Address);

//            tenant.ContactPersonName = NormalizeNullable(dto.ContactPersonName);
//            tenant.ContactPersonDesignation = NormalizeNullable(dto.ContactPersonDesignation);
//            tenant.ContactPersonEmail = NormalizeNullable(dto.ContactPersonEmail);

//            tenant.ShortName = NormalizeNullable(dto.ShortName);
//            tenant.TimeZone = string.IsNullOrWhiteSpace(dto.TimeZone) ? "Asia/Dhaka" : dto.TimeZone.Trim();
//            tenant.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "BDT" : dto.Currency.Trim();

//            tenant.Country = NormalizeNullable(dto.Country);
//            tenant.Division = NormalizeNullable(dto.Division);
//            tenant.District = NormalizeNullable(dto.District);
//            tenant.Thana = NormalizeNullable(dto.Thana);
//            tenant.PostCode = NormalizeNullable(dto.PostCode);

//            tenant.Subdomain = NormalizeNullable(dto.Subdomain)?.ToLowerInvariant();
//            tenant.CustomDomain = NormalizeNullable(dto.CustomDomain)?.ToLowerInvariant();

//            tenant.PrimaryColor = NormalizeNullable(dto.PrimaryColor);
//            tenant.SecondaryColor = NormalizeNullable(dto.SecondaryColor);
//            tenant.WebsiteUrl = NormalizeNullable(dto.WebsiteUrl);

//            tenant.EIIN = NormalizeNullable(dto.EIIN);
//            tenant.RegistrationNumber = NormalizeNullable(dto.RegistrationNumber);
//            tenant.EducationBoard = NormalizeNullable(dto.EducationBoard);
//            tenant.EstablishedDate = dto.EstablishedDate;

//            tenant.InstitutionCode = NormalizeNullable(dto.InstitutionCode);

//            tenant.Language = string.IsNullOrWhiteSpace(dto.Language) ? "en" : dto.Language.Trim();
//            tenant.DateFormat = string.IsNullOrWhiteSpace(dto.DateFormat) ? "dd-MMM-yyyy" : dto.DateFormat.Trim();

//            if (dto.LogoFile != null && dto.LogoFile.Length > 0)
//            {
//                var logoPath = await SaveTenantImageAsync(
//                    dto.LogoFile,
//                    "logos",
//                    tenant.Id,
//                    new[] { ".jpg", ".jpeg", ".png", ".webp" },
//                    2 * 1024 * 1024);

//                if (logoPath == null)
//                    return false;

//                DeleteOldFileIfExists(tenant.LogoUrl);
//                tenant.LogoUrl = logoPath;
//            }

//            if (dto.FaviconFile != null && dto.FaviconFile.Length > 0)
//            {
//                var faviconPath = await SaveTenantImageAsync(
//                    dto.FaviconFile,
//                    "favicons",
//                    tenant.Id,
//                    new[] { ".ico", ".png", ".jpg", ".jpeg", ".webp", ".svg" },
//                    1 * 1024 * 1024);

//                if (faviconPath == null)
//                    return false;

//                DeleteOldFileIfExists(tenant.FaviconUrl);
//                tenant.FaviconUrl = faviconPath;
//            }

//            tenant.IsSetupCompleted = true;
//            tenant.SetupCompletedAt = DateTime.UtcNow;
//            tenant.Status = "Active";

//            if (tenant.CurrentOnboardingStep < 3)
//                tenant.CurrentOnboardingStep = 3;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id);

//            if (onboarding != null)
//            {
//                onboarding.InstitutionProfileCompleted = true;

//                if (onboarding.CurrentStep < 3)
//                    onboarding.CurrentStep = 3;
//            }

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }

//        private static string? NormalizeNullable(string? value)
//        {
//            if (string.IsNullOrWhiteSpace(value))
//                return null;

//            return value.Trim();
//        }

//        private async Task<string?> SaveTenantImageAsync(
//            Microsoft.AspNetCore.Http.IFormFile file,
//            string folderName,
//            long tenantId,
//            string[] allowedExtensions,
//            long maxBytes)
//        {
//            if (file == null || file.Length <= 0)
//                return null;

//            if (file.Length > maxBytes)
//                return null;

//            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
//            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
//                return null;

//            var rootPath = _environment.WebRootPath;
//            if (string.IsNullOrWhiteSpace(rootPath))
//                return null;

//            var folderPath = Path.Combine(rootPath, "uploads", "tenants", folderName);
//            if (!Directory.Exists(folderPath))
//                Directory.CreateDirectory(folderPath);

//            var fileName = $"tenant_{tenantId}_{Guid.NewGuid():N}{extension}";
//            var physicalPath = Path.Combine(folderPath, fileName);

//            using (var stream = new FileStream(physicalPath, FileMode.Create))
//            {
//                await file.CopyToAsync(stream);
//            }

//            return $"/uploads/tenants/{folderName}/{fileName}";
//        }

//        private void DeleteOldFileIfExists(string? relativePath)
//        {
//            if (string.IsNullOrWhiteSpace(relativePath))
//                return;

//            var cleanedPath = relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
//            var physicalPath = Path.Combine(_environment.WebRootPath, cleanedPath);

//            if (File.Exists(physicalPath))
//            {
//                try
//                {
//                    File.Delete(physicalPath);
//                }
//                catch
//                {
//                }
//            }
//        }


//        // Campus
//        public async Task<List<CampusListItemDto>> GetCampusListAsync()
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return new List<CampusListItemDto>();

//            var campuses = await _unitOfWork.Campuses.FindAsync(x => x.TenantId == tenantId.Value && !x.IsDeleted);

//            return campuses
//                .OrderBy(x => x.DisplayOrder)
//                .ThenByDescending(x => x.IsMainCampus)
//                .Select(x => new CampusListItemDto
//                {
//                    Id = x.Id,
//                    Name = x.Name,
//                    Code = x.Code,
//                    CampusType = x.CampusType,
//                    ContactNumber = x.ContactNumber,
//                    Email = x.Email,
//                    Address = x.Address,
//                    IsMainCampus = x.IsMainCampus,
//                    IsActive = x.IsActive,
//                    DisplayOrder = x.DisplayOrder
//                })
//                .ToList();
//        }

//        public async Task<CampusSetupDto?> GetCampusByIdAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return null;

//            var campus = await _unitOfWork.Campuses.FirstOrDefaultAsync(x =>
//                x.Id == id &&
//                x.TenantId == tenantId.Value &&
//                !x.IsDeleted);

//            if (campus == null) return null;

//            return new CampusSetupDto
//            {
//                Id = campus.Id,
//                Name = campus.Name,
//                Code = campus.Code,
//                CampusType = campus.CampusType,
//                ContactNumber = campus.ContactNumber,
//                Email = campus.Email,
//                Country = campus.Country,
//                Division = campus.Division,
//                District = campus.District,
//                Thana = campus.Thana,
//                PostCode = campus.PostCode,
//                Address = campus.Address,
//                PrincipalName = campus.PrincipalName,
//                HeadName = campus.HeadName,
//                IsMainCampus = campus.IsMainCampus,
//                IsActive = campus.IsActive,
//                DisplayOrder = campus.DisplayOrder
//            };
//        }

//        public async Task<bool> SaveCampusAsync(CampusSetupDto dto)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            Campus? campus = null;

//            if (dto.Id.HasValue && dto.Id.Value > 0)
//            {
//                campus = await _unitOfWork.Campuses.FirstOrDefaultAsync(x =>
//                    x.Id == dto.Id.Value &&
//                    x.TenantId == tenantId.Value &&
//                    !x.IsDeleted);

//                if (campus == null) return false;
//            }
//            else
//            {
//                campus = new Campus
//                {
//                    TenantId = tenantId.Value
//                };

//                await _unitOfWork.Campuses.AddAsync(campus);
//            }

//            if (dto.IsMainCampus)
//            {
//                var allCampuses = await _unitOfWork.Campuses.FindAsync(x =>
//                    x.TenantId == tenantId.Value &&
//                    !x.IsDeleted);

//                foreach (var item in allCampuses)
//                {
//                    item.IsMainCampus = false;
//                }
//            }

//            campus.Name = dto.Name.Trim();
//            campus.Code = NormalizeNullable(dto.Code);
//            campus.CampusType = NormalizeNullable(dto.CampusType);
//            campus.ContactNumber = NormalizeNullable(dto.ContactNumber);
//            campus.Email = NormalizeNullable(dto.Email);

//            campus.Country = NormalizeNullable(dto.Country);
//            campus.Division = NormalizeNullable(dto.Division);
//            campus.District = NormalizeNullable(dto.District);
//            campus.Thana = NormalizeNullable(dto.Thana);
//            campus.PostCode = NormalizeNullable(dto.PostCode);
//            campus.Address = NormalizeNullable(dto.Address);

//            campus.PrincipalName = NormalizeNullable(dto.PrincipalName);
//            campus.HeadName = NormalizeNullable(dto.HeadName);

//            campus.IsMainCampus = dto.IsMainCampus;
//            campus.IsActive = dto.IsActive;
//            campus.DisplayOrder = dto.DisplayOrder <= 0 ? 1 : dto.DisplayOrder;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value);

//            if (onboarding != null)
//            {
//                onboarding.CampusSetupCompleted = true;

//                if (onboarding.CurrentStep < 4)
//                    onboarding.CurrentStep = 4;
//            }

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value);
//            if (tenant != null && tenant.CurrentOnboardingStep < 4)
//            {
//                tenant.CurrentOnboardingStep = 4;
//            }

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }

//        public async Task<bool> DeleteCampusAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            var campus = await _unitOfWork.Campuses.FirstOrDefaultAsync(x =>
//                x.Id == id &&
//                x.TenantId == tenantId.Value &&
//                !x.IsDeleted);

//            if (campus == null) return false;

//            campus.IsDeleted = true;
//            campus.IsActive = false;

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }



//        //Academic Year
//        public async Task<List<AcademicYearListItemDto>> GetAcademicYearListAsync()
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return new List<AcademicYearListItemDto>();

//            var items = await _unitOfWork.AcademicYears.FindAsync(x => x.TenantId == tenantId.Value && !x.IsDeleted);

//            return items
//                .OrderBy(x => x.DisplayOrder)
//                .ThenByDescending(x => x.IsCurrent)
//                .ThenByDescending(x => x.StartDate)
//                .Select(x => new AcademicYearListItemDto
//                {
//                    Id = x.Id,
//                    Name = x.Name,
//                    StartDate = x.StartDate,
//                    EndDate = x.EndDate,
//                    IsCurrent = x.IsCurrent,
//                    IsActive = x.IsActive,
//                    DisplayOrder = x.DisplayOrder
//                })
//                .ToList();
//        }

//        public async Task<AcademicYearSetupDto?> GetAcademicYearByIdAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return null;

//            var item = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(x =>
//                x.Id == id && x.TenantId == tenantId.Value && !x.IsDeleted);

//            if (item == null) return null;

//            return new AcademicYearSetupDto
//            {
//                Id = item.Id,
//                Name = item.Name,
//                StartDate = item.StartDate,
//                EndDate = item.EndDate,
//                IsCurrent = item.IsCurrent,
//                IsActive = item.IsActive,
//                DisplayOrder = item.DisplayOrder
//            };
//        }

//        public async Task<bool> SaveAcademicYearAsync(AcademicYearSetupDto dto)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            if (dto.StartDate > dto.EndDate) return false;

//            AcademicYear? item = null;

//            if (dto.Id.HasValue && dto.Id.Value > 0)
//            {
//                item = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(x =>
//                    x.Id == dto.Id.Value &&
//                    x.TenantId == tenantId.Value &&
//                    !x.IsDeleted);

//                if (item == null) return false;
//            }
//            else
//            {
//                item = new AcademicYear
//                {
//                    TenantId = tenantId.Value
//                };

//                await _unitOfWork.AcademicYears.AddAsync(item);
//            }

//            if (dto.IsCurrent)
//            {
//                var allYears = await _unitOfWork.AcademicYears.FindAsync(x =>
//                    x.TenantId == tenantId.Value && !x.IsDeleted);

//                foreach (var year in allYears)
//                {
//                    year.IsCurrent = false;
//                }
//            }

//            item.Name = dto.Name.Trim();
//            item.StartDate = dto.StartDate;
//            item.EndDate = dto.EndDate;
//            item.IsCurrent = dto.IsCurrent;
//            item.IsActive = dto.IsActive;
//            item.DisplayOrder = dto.DisplayOrder <= 0 ? 1 : dto.DisplayOrder;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value);

//            if (onboarding != null)
//            {
//                onboarding.AcademicSetupCompleted = true;

//                if (onboarding.CurrentStep < 5)
//                    onboarding.CurrentStep = 5;
//            }

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value);
//            if (tenant != null && tenant.CurrentOnboardingStep < 5)
//            {
//                tenant.CurrentOnboardingStep = 5;
//            }

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }

//        public async Task<bool> DeleteAcademicYearAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            var item = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(x =>
//                x.Id == id && x.TenantId == tenantId.Value && !x.IsDeleted);

//            if (item == null) return false;

//            var hasTerms = await _unitOfWork.AcademicTerms.AnyAsync(x =>
//                x.AcademicYearId == item.Id &&
//                x.TenantId == tenantId.Value &&
//                !x.IsDeleted);

//            if (hasTerms) return false;

//            item.IsDeleted = true;
//            item.IsActive = false;

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }

//        public async Task<List<AcademicTermListItemDto>> GetAcademicTermListAsync()
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return new List<AcademicTermListItemDto>();

//            var terms = await _unitOfWork.AcademicTerms.FindAsync(x => x.TenantId == tenantId.Value && !x.IsDeleted);
//            var years = await _unitOfWork.AcademicYears.FindAsync(x => x.TenantId == tenantId.Value && !x.IsDeleted);

//            return terms
//                .OrderBy(x => x.DisplayOrder)
//                .ThenByDescending(x => x.IsCurrent)
//                .ThenBy(x => x.StartDate)
//                .Select(x => new AcademicTermListItemDto
//                {
//                    Id = x.Id,
//                    AcademicYearId = x.AcademicYearId,
//                    AcademicYearName = years.FirstOrDefault(y => y.Id == x.AcademicYearId)?.Name ?? "",
//                    Name = x.Name,
//                    TermType = x.TermType,
//                    StartDate = x.StartDate,
//                    EndDate = x.EndDate,
//                    IsCurrent = x.IsCurrent,
//                    IsActive = x.IsActive,
//                    DisplayOrder = x.DisplayOrder
//                })
//                .ToList();
//        }

//        public async Task<AcademicTermSetupDto?> GetAcademicTermByIdAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return null;

//            var item = await _unitOfWork.AcademicTerms.FirstOrDefaultAsync(x =>
//                x.Id == id && x.TenantId == tenantId.Value && !x.IsDeleted);

//            if (item == null) return null;

//            return new AcademicTermSetupDto
//            {
//                Id = item.Id,
//                AcademicYearId = item.AcademicYearId,
//                Name = item.Name,
//                TermType = item.TermType,
//                StartDate = item.StartDate,
//                EndDate = item.EndDate,
//                IsCurrent = item.IsCurrent,
//                IsActive = item.IsActive,
//                DisplayOrder = item.DisplayOrder
//            };
//        }

//        public async Task<bool> SaveAcademicTermAsync(AcademicTermSetupDto dto)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            if (dto.StartDate > dto.EndDate) return false;

//            var year = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(x =>
//                x.Id == dto.AcademicYearId &&
//                x.TenantId == tenantId.Value &&
//                !x.IsDeleted);

//            if (year == null) return false;

//            AcademicTerm? item = null;

//            if (dto.Id.HasValue && dto.Id.Value > 0)
//            {
//                item = await _unitOfWork.AcademicTerms.FirstOrDefaultAsync(x =>
//                    x.Id == dto.Id.Value &&
//                    x.TenantId == tenantId.Value &&
//                    !x.IsDeleted);

//                if (item == null) return false;
//            }
//            else
//            {
//                item = new AcademicTerm
//                {
//                    TenantId = tenantId.Value
//                };

//                await _unitOfWork.AcademicTerms.AddAsync(item);
//            }

//            if (dto.IsCurrent)
//            {
//                var allTerms = await _unitOfWork.AcademicTerms.FindAsync(x =>
//                    x.TenantId == tenantId.Value &&
//                    !x.IsDeleted);

//                foreach (var term in allTerms)
//                {
//                    term.IsCurrent = false;
//                }
//            }

//            item.AcademicYearId = dto.AcademicYearId;
//            item.Name = dto.Name.Trim();
//            item.TermType = NormalizeNullable(dto.TermType);
//            item.StartDate = dto.StartDate;
//            item.EndDate = dto.EndDate;
//            item.IsCurrent = dto.IsCurrent;
//            item.IsActive = dto.IsActive;
//            item.DisplayOrder = dto.DisplayOrder <= 0 ? 1 : dto.DisplayOrder;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value);

//            if (onboarding != null)
//            {
//                onboarding.AcademicSetupCompleted = true;

//                if (onboarding.CurrentStep < 5)
//                    onboarding.CurrentStep = 5;
//            }

//            var tenant = await _unitOfWork.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId.Value);
//            if (tenant != null && tenant.CurrentOnboardingStep < 5)
//            {
//                tenant.CurrentOnboardingStep = 5;
//            }

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }

//        public async Task<bool> DeleteAcademicTermAsync(long id)
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            var item = await _unitOfWork.AcademicTerms.FirstOrDefaultAsync(x =>
//                x.Id == id && x.TenantId == tenantId.Value && !x.IsDeleted);

//            if (item == null) return false;

//            item.IsDeleted = true;
//            item.IsActive = false;

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }


//        public async Task<bool> FinalCompleteAsync()
//        {
//            var tenantId = await UserContext.ResolveTenantIdIntAsync();
//            if (tenantId == null) return false;

//            var onboarding = await _unitOfWork.OnboardingProgresses
//                .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value);

//            if (onboarding == null) return false;

//            // required previous steps check
//            if (!onboarding.AccountCreated ||
//                !onboarding.EmailVerified ||
//                !onboarding.InstitutionProfileCompleted ||
//                !onboarding.CampusSetupCompleted ||
//                !onboarding.AcademicSetupCompleted)
//            {
//                return false;
//            }

//            // যদি role/permission step আলাদা রাখেন, এটা uncomment রাখুন
//            if (!onboarding.RolePermissionSetupCompleted)
//            {
//                return false;
//            }

//            onboarding.FinalCompleted = true;
//            onboarding.CurrentStep = 7;
//            onboarding.CompletedAt = DateTime.UtcNow;

//            var tenant = await _unitOfWork.Tenants
//                .FirstOrDefaultAsync(x => x.Id == tenantId.Value);

//            if (tenant != null)
//            {
//                tenant.IsSetupCompleted = true;
//                tenant.SetupCompletedAt = DateTime.UtcNow;
//                tenant.CurrentOnboardingStep = 7;
//                tenant.Status = "Active";
//            }

//            await _unitOfWork.SaveChangesAsync();
//            return true;
//        }
//    }
//}