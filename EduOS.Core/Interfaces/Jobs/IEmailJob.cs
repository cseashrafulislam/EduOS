using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Interfaces.Jobs
{
    public interface IEmailJob
    {
        Task SendVerificationEmailAsync(
            string toEmail,
            string institutionName,
            string ownerName,
            string verifyUrl);

        Task SendVerificationSuccessEmailAsync(
            string toEmail,
            string institutionName,
            string fullName,
            string userName,
            string loginUrl,
            string setPasswordUrl);

        Task SendPasswordResetEmailAsync(
            string toEmail,
            string fullName,
            string resetUrl);

    }
}
