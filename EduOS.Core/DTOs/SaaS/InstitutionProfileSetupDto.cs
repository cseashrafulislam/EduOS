using Microsoft.AspNetCore.Http;
using System;

namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionProfileSetupDto
    {
        public string InstitutionName { get; set; } = string.Empty;
        public string? InstitutionType { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string? OwnerPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerDesignation { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? PostalCode { get; set; }
        public string Email { get; set; } = string.Empty;
    }
}