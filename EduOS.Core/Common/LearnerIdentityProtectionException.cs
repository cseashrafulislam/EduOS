namespace EduOS.Core.Common;

public sealed class LearnerIdentityProtectionException : Exception
{
    public LearnerIdentityProtectionException(string message)
        : base(message)
    {
    }

    public LearnerIdentityProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
