namespace EduOS.Core.Enums;

public enum AdmissionIntakeFormStatus
{
    Draft = 1,
    Published = 2,
    Closed = 3,
    Archived = 4
}

public enum AdmissionFormFieldType
{
    Text = 1,
    TextArea = 2,
    Number = 3,
    Date = 4,
    Choice = 5,
    Boolean = 6
}

public enum AdmissionDocumentVerificationStatus
{
    Pending = 1,
    Verified = 2,
    Rejected = 3
}
