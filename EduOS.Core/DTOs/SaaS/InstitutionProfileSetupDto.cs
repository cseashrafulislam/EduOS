using Microsoft.AspNetCore.Http;
using System;

namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionProfileSetupDto
    {
        public string InstitutionName { get; set; } = string.Empty;
        public string InstitutionType { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public string? Address { get; set; }

        public string? ContactPersonName { get; set; }
        public string? ContactPersonDesignation { get; set; }
        public string? ContactPersonEmail { get; set; }

        public string? ShortName { get; set; }
        public string? TimeZone { get; set; }
        public string? Currency { get; set; }

        public string? Country { get; set; }
        public string? Division { get; set; }
        public string? District { get; set; }
        public string? Thana { get; set; }
        public string? PostCode { get; set; }

        public string? Subdomain { get; set; }
        public string? CustomDomain { get; set; }

        public IFormFile? LogoFile { get; set; }
        public IFormFile? FaviconFile { get; set; }

        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }

        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? WebsiteUrl { get; set; }

        public string? EIIN { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? EducationBoard { get; set; }
        public DateTime? EstablishedDate { get; set; }

        public string? InstitutionCode { get; set; }

        public string? Language { get; set; }
        public string? DateFormat { get; set; }
    }
}