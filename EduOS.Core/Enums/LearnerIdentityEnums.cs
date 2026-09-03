namespace EduOS.Core.Enums;

public enum PersonIdentifierType
{
    BirthRegistration = 1,
    NationalId = 2
}

public enum IdentifierVerificationStatus
{
    Unverified = 0,
    Pending = 1,
    Verified = 2,
    Rejected = 3
}

public enum StudentPersonLinkStatus
{
    Active = 1,
    Disputed = 2,
    Revoked = 3
}

[Flags]
public enum LearnerDataScope
{
    None = 0,
    BasicIdentity = 1,
    InstitutionMembershipHistory = 2,
    AcademicSummary = 4
}

public enum LearnerIdentityPurpose
{
    Admission = 1,
    Transfer = 2,
    IdentityVerification = 3,
    ContinuingEducation = 4
}

public enum LearnerConsentRequestStatus
{
    Pending = 1,
    Approved = 2,
    Denied = 3,
    Expired = 4,
    Revoked = 5
}

public enum LearnerIdentityAccessAction
{
    RegisterOrLink = 1,
    RequestConsent = 2
}

public enum LearnerIdentityAccessOutcome
{
    Created = 1,
    Reused = 2,
    ConsentRequired = 3,
    Denied = 4,
    Failed = 5
}
