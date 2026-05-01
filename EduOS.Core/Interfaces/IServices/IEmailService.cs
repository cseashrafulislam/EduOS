using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Interfaces.IServices
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    }
}
