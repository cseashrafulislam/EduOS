using EduOS.Core.Enums.Interfaces.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.Jobs;

namespace EduOS.BackgroundJobs.Jobs
{
    public class EmailJob : IEmailJob
    {
        private readonly IEmailService _emailService;

        public EmailJob(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public async Task SendVerificationEmailAsync(
            string toEmail,
            string institutionName,
            string ownerName,
            string verifyUrl)
        {
            var subject = "Verify your EduOS account";

            var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <h2>Welcome to EduOS</h2>
                <p>Dear {ownerName},</p>
                <p>Thank you for registering <b>{institutionName}</b> in EduOS.</p>
                <p>Please verify your email address by clicking the button below:</p>
                <p>
                    <a href='{verifyUrl}' 
                       style='background:#2563eb;color:#fff;padding:10px 18px;text-decoration:none;border-radius:6px;display:inline-block;'>
                       Verify Email
                    </a>
                </p>
                <p>If the button does not work, copy and paste this link into your browser:</p>
                <p>{verifyUrl}</p>
                <br />
                <p>Regards,<br/>EduOS Team</p>
            </body>
            </html>";

            await _emailService.SendEmailAsync(toEmail, subject, body, true);
        }

        public async Task SendVerificationSuccessEmailAsync(
            string toEmail,
            string institutionName,
            string fullName,
            string userName,
            string loginUrl,
            string setPasswordUrl)
        {
            var subject = "EduOS account verified successfully";

            var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <h2>Email Verified Successfully</h2>
                <p>Dear {fullName},</p>
                <p>Your institution <b>{institutionName}</b> has been verified successfully.</p>

                <p><b>Login Information:</b></p>
                <ul>
                    <li><b>Username:</b> {userName}</li>
                </ul>

                <p><b>Security Note:</b> For your security, password is not sent by email.</p>

                <p>You can set or reset your password from the link below:</p>
                <p>
                    <a href='{setPasswordUrl}' 
                       style='background:#16a34a;color:#fff;padding:10px 18px;text-decoration:none;border-radius:6px;display:inline-block;'>
                       Set Password
                    </a>
                </p>

                <p>Then login here:</p>
                <p>
                    <a href='{loginUrl}' 
                       style='background:#2563eb;color:#fff;padding:10px 18px;text-decoration:none;border-radius:6px;display:inline-block;'>
                       Login to EduOS
                    </a>
                </p>

                <p>If needed, you can open the login page directly:</p>
                <p>{loginUrl}</p>

                <br />
                <p>Regards,<br/>EduOS Team</p>
            </body>
            </html>";

            await _emailService.SendEmailAsync(toEmail, subject, body, true);
        }

        public async Task SendPasswordResetEmailAsync(
            string toEmail,
            string fullName,
            string resetUrl)
        {
            var subject = "Reset your EduOS password";

            var body = $@"
            <html>
            <body style='font-family:Arial, sans-serif;'>
                <h2>Password Reset Request</h2>
                <p>Hello {fullName},</p>
                <p>You requested to reset your password.</p>
                <p>
                    <a href='{resetUrl}'
                       style='background:#2563eb;color:white;padding:10px 18px;text-decoration:none;border-radius:6px;display:inline-block;'>
                       Reset Password
                    </a>
                </p>
                <p>If the button does not work, open this link:</p>
                <p>{resetUrl}</p>
                <br/>
                <p>EduOS Team</p>
            </body>
            </html>";

            await _emailService.SendEmailAsync(toEmail, subject, body, true);
        }
    }
}