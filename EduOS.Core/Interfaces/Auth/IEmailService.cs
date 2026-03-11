using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Enums.Interfaces.Auth
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    }
}
