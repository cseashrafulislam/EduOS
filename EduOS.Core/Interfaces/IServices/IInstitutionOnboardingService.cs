using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IInstitutionOnboardingService
    {
        Task<InstitutionSignupResponseDto> RegisterInstitutionAsync(InstitutionSignupRequestDto dto, string baseUrl);
        Task<bool> VerifyEmailAsync(string email, string token, string baseUrl);
        Task<bool> SaveInstitutionProfileAsync(InstitutionProfileSetupDto dto);
        Task<InstitutionProfileSetupDto?> GetInstitutionProfileAsync();

        Task<List<CampusListItemDto>> GetCampusListAsync();
        Task<CampusSetupDto?> GetCampusByIdAsync(long id);
        Task<bool> SaveCampusAsync(CampusSetupDto dto);
        Task<bool> DeleteCampusAsync(long id);



        Task<List<AcademicYearListItemDto>> GetAcademicYearListAsync();
        Task<AcademicYearSetupDto?> GetAcademicYearByIdAsync(long id);
        Task<bool> SaveAcademicYearAsync(AcademicYearSetupDto dto);
        Task<bool> DeleteAcademicYearAsync(long id);

        Task<List<AcademicTermListItemDto>> GetAcademicTermListAsync();
        Task<AcademicTermSetupDto?> GetAcademicTermByIdAsync(long id);
        Task<bool> SaveAcademicTermAsync(AcademicTermSetupDto dto);
        Task<bool> DeleteAcademicTermAsync(long id);

        
        Task<bool> FinalCompleteAsync();
    }
}