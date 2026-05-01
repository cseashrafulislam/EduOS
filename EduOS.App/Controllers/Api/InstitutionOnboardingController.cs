using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [Route("api/institution-onboarding")]
    public class InstitutionOnboardingController : ControllerBase
    {
        private readonly IInstitutionOnboardingService _service;

        public InstitutionOnboardingController(IInstitutionOnboardingService service)
        {
            _service = service;
        }

        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] InstitutionSignupRequestDto dto)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var result = await _service.RegisterInstitutionAsync(dto, baseUrl);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var ok = await _service.VerifyEmailAsync(email, token, baseUrl);

            if (!ok)
                return Redirect($"{baseUrl}/Account/VerifyFailed");

            return Redirect($"{baseUrl}/Account/VerifyEmailSuccess");
        }

        [HttpGet("institution-profile")]
        public async Task<IActionResult> GetInstitutionProfile()
        {
            var data = await _service.GetInstitutionProfileAsync();

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Institution profile not found."
                });
            }

            return Ok(new
            {
                success = true,
                data
            });
        }

        [HttpPost("institution-profile")]
        public async Task<IActionResult> SaveInstitutionProfile([FromForm] InstitutionProfileSetupDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Invalid request."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.InstitutionName))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Institution name is required."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.InstitutionType))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Institution type is required."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.OwnerName))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Owner name is required."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Email is required."
                });
            }

            var ok = await _service.SaveInstitutionProfileAsync(dto);

            if (!ok)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Institution profile save failed."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Institution profile saved successfully."
            });
        }

        [HttpGet("campus-list")]
        public async Task<IActionResult> GetCampusList()
        {
            var data = await _service.GetCampusListAsync();
            return Ok(data);
        }

        [HttpGet("campus/{id:long}")]
        public async Task<IActionResult> GetCampus(long id)
        {
            var data = await _service.GetCampusByIdAsync(id);

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Campus not found."
                });
            }

            return Ok(data);
        }

        [HttpPost("campus")]
        public async Task<IActionResult> SaveCampus([FromBody] CampusSetupDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Campus name is required." });

            var ok = await _service.SaveCampusAsync(dto);

            if (!ok)
                return BadRequest(new { success = false, message = "Campus save failed." });

            return Ok(new
            {
                success = true,
                message = "Campus saved successfully."
            });
        }

        [HttpDelete("campus/{id:long}")]
        public async Task<IActionResult> DeleteCampus(long id)
        {
            var ok = await _service.DeleteCampusAsync(id);

            if (!ok)
                return BadRequest(new { success = false, message = "Campus delete failed." });

            return Ok(new
            {
                success = true,
                message = "Campus deleted successfully."
            });
        }



        [HttpGet("academic-years")]
        public async Task<IActionResult> GetAcademicYears()
        {
            var data = await _service.GetAcademicYearListAsync();
            return Ok(data);
        }

        [HttpGet("academic-year/{id:long}")]
        public async Task<IActionResult> GetAcademicYear(long id)
        {
            var data = await _service.GetAcademicYearByIdAsync(id);

            if (data == null)
                return NotFound(new { success = false, message = "Academic year not found." });

            return Ok(data);
        }

        [HttpPost("academic-year")]
        public async Task<IActionResult> SaveAcademicYear([FromBody] AcademicYearSetupDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Academic year name is required." });

            var ok = await _service.SaveAcademicYearAsync(dto);

            if (!ok)
                return BadRequest(new { success = false, message = "Academic year save failed." });

            return Ok(new { success = true, message = "Academic year saved successfully." });
        }

        [HttpDelete("academic-year/{id:long}")]
        public async Task<IActionResult> DeleteAcademicYear(long id)
        {
            var ok = await _service.DeleteAcademicYearAsync(id);

            if (!ok)
                return BadRequest(new { success = false, message = "Academic year delete failed. Remove terms first." });

            return Ok(new { success = true, message = "Academic year deleted successfully." });
        }

        [HttpGet("academic-terms")]
        public async Task<IActionResult> GetAcademicTerms()
        {
            var data = await _service.GetAcademicTermListAsync();
            return Ok(data);
        }

        [HttpGet("academic-term/{id:long}")]
        public async Task<IActionResult> GetAcademicTerm(long id)
        {
            var data = await _service.GetAcademicTermByIdAsync(id);

            if (data == null)
                return NotFound(new { success = false, message = "Academic term not found." });

            return Ok(data);
        }

        [HttpPost("academic-term")]
        public async Task<IActionResult> SaveAcademicTerm([FromBody] AcademicTermSetupDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            if (dto.AcademicYearId <= 0)
                return BadRequest(new { success = false, message = "Academic year is required." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Academic term name is required." });

            var ok = await _service.SaveAcademicTermAsync(dto);

            if (!ok)
                return BadRequest(new { success = false, message = "Academic term save failed." });

            return Ok(new { success = true, message = "Academic term saved successfully." });
        }

        [HttpDelete("academic-term/{id:long}")]
        public async Task<IActionResult> DeleteAcademicTerm(long id)
        {
            var ok = await _service.DeleteAcademicTermAsync(id);

            if (!ok)
                return BadRequest(new { success = false, message = "Academic term delete failed." });

            return Ok(new { success = true, message = "Academic term deleted successfully." });
        }



        [HttpPost("final-complete")]
        public async Task<IActionResult> FinalComplete()
        {
            var ok = await _service.FinalCompleteAsync();

            if (!ok)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Final onboarding completion failed. Please complete previous steps first."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Onboarding completed successfully."
            });
        }
    }
}