namespace EduOS.Core.Constants
{
    public static class AppConstants
    {
        public const string DefaultTimeZone = "Asia/Dhaka";
        public const string DefaultDateFormat = "dd/MM/yyyy";
        public const string DefaultCurrency = "BDT";
        public const string DefaultLanguage = "en";
        
        public const int DefaultPageSize = 10;
        public const int MaxPageSize = 100;
        
        public const int AccessTokenExpiryMinutes = 60;
        public const int RefreshTokenExpiryDays = 7;
        public const int MaxFailedLoginAttempts = 5;
        public const int LockoutMinutes = 30;
        
        public const int OtpExpiryMinutes = 5;
        public const int PasswordMinLength = 8;
    }

    public static class FileConstants
    {
        public const int MaxFileSizeMB = 10;
        public static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
        public static readonly string[] AllowedDocumentExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
    }

    public static class CacheKeys
    {
        public const string TenantSettings = "tenant:{0}:settings";
        public const string UserPermissions = "user:{0}:permissions";
        public const string ActiveClasses = "tenant:{0}:classes:active";
        public const string GradeRules = "tenant:{0}:graderules";
    }
}
