using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IInstitutionOnboardingService
    {
        // ── Signup + Email Verify ──────────────────────────────
        Task<ApiResponse<InstitutionSignupResponseDto>> RegisterInstitutionAsync(
            InstitutionSignupRequestDto dto, string baseUrl);

        Task<bool> VerifyEmailAsync(string email, string token, string baseUrl);

        // ── Institution Profile ────────────────────────────────
        Task<ApiResponse<InstitutionProfileSetupDto?>> GetInstitutionProfileAsync();
        Task<ApiResponse<bool>> SaveInstitutionProfileAsync(InstitutionProfileSetupDto dto);

        // ── Campus ────────────────────────────────────────────
        Task<ApiResponse<List<CampusListItemDto>>> GetCampusListAsync();
        Task<ApiResponse<CampusSetupDto?>> GetCampusByIdAsync(long id);
        Task<ApiResponse<bool>> SaveCampusAsync(CampusSetupDto dto);
        Task<ApiResponse<bool>> DeleteCampusAsync(long id);

        // ── Academic Year ──────────────────────────────────────
        Task<ApiResponse<List<AcademicYearListItemDto>>> GetAcademicYearListAsync();
        Task<ApiResponse<AcademicYearSetupDto?>> GetAcademicYearByIdAsync(long id);
        Task<ApiResponse<bool>> SaveAcademicYearAsync(AcademicYearSetupDto dto);
        Task<ApiResponse<bool>> DeleteAcademicYearAsync(long id);

        // ── Academic Term ──────────────────────────────────────
        Task<ApiResponse<List<AcademicTermListItemDto>>> GetAcademicTermListAsync();
        Task<ApiResponse<AcademicTermSetupDto?>> GetAcademicTermByIdAsync(long id);
        Task<ApiResponse<bool>> SaveAcademicTermAsync(AcademicTermSetupDto dto);
        Task<ApiResponse<bool>> DeleteAcademicTermAsync(long id);

        // ── Final Complete ─────────────────────────────────────
        Task<ApiResponse<bool>> FinalCompleteAsync();
    }
}
