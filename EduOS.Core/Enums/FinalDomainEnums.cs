namespace EduOS.Core.Enums.Domain;

public enum TenantState { PendingVerification = 1, Active = 2, Suspended = 3, Closed = 4 }
public enum OnboardingStage { EmailVerification = 1, InstitutionProfile = 2, PlanSelection = 3, Payment = 4, CampusSetup = 5, AcademicSetup = 6, ModuleSetup = 7, BrandingSetup = 8, GeneralSettings = 9, GatewaySetup = 10, Completed = 11 }
public enum MembershipStatus { Invited = 1, Active = 2, Suspended = 3, Left = 4 }
public enum SubscriptionState { Trial = 1, Active = 2, Grace = 3, Suspended = 4, Cancelled = 5, Expired = 6, PendingPayment = 7 }
public enum InvoiceState { Draft = 1, Issued = 2, PartiallyPaid = 3, Paid = 4, Cancelled = 5, Refunded = 6 }
public enum PaymentState { Initiated = 1, AwaitingVerification = 2, Successful = 3, Failed = 4, Cancelled = 5, Refunded = 6 }
public enum PaymentMethodType { Cash = 1, BankTransfer = 2, Card = 3, MobileFinancialService = 4, Gateway = 5, Cheque = 6, Other = 99 }
public enum AcademicCycleType { Annual = 1, Semester = 2, Trimester = 3, Quarterly = 4, Modular = 5, BatchBased = 6 }
public enum DeliveryModeType { OnCampus = 1, OnlineLive = 2, OnlineSelfPaced = 3, Hybrid = 4 }
public enum EnrollmentState { Active = 1, Completed = 2, Promoted = 3, Transferred = 4, Withdrawn = 5, Suspended = 6, Dropped = 7 }
public enum SubjectRegistrationState { Pending = 1, Approved = 2, Rejected = 3, Dropped = 4, Completed = 5 }
public enum AttendanceState { Present = 1, Absent = 2, Late = 3, Leave = 4, Excused = 5 }
public enum CalendarEventKind { Academic = 1, Holiday = 2, Examination = 3, Admission = 4, Sports = 5, Cultural = 6, Meeting = 7, Other = 99 }
public enum LessonPlanState { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, InProgress = 5, Completed = 6 }
public enum AdmissionFormState { Draft = 1, Published = 2, Closed = 3, Archived = 4 }
public enum AdmissionApplicantState { Draft = 1, Submitted = 2, UnderReview = 3, DocumentPending = 4, AssessmentPending = 5, Qualified = 6, Rejected = 7, Admitted = 8, Withdrawn = 9 }
public enum AdmissionDecisionState { Pending = 1, Offered = 2, Accepted = 3, Rejected = 4, Expired = 5, Cancelled = 6 }
public enum AssessmentKind { Exam = 1, Quiz = 2, Assignment = 3, ClassTest = 4, Practical = 5, Viva = 6, Project = 7, ContinuousAssessment = 8 }
public enum AssessmentState { Draft = 1, Scheduled = 2, InProgress = 3, MarksEntry = 4, Locked = 5, Published = 6, Cancelled = 7 }
public enum ResultPublicationState { Draft = 1, Published = 2, Withdrawn = 3 }
public enum StudentProgressionDecisionType { Promoted = 1, Repeated = 2 }
public enum StudentExitType { Transfer = 1, Completed = 2, Dropout = 3, Withdrawn = 4 }
public enum TransferRequestState { Draft = 1, Submitted = 2, SourceApproved = 3, DestinationAccepted = 4, Rejected = 5, Completed = 6, Cancelled = 7 }
public enum PersonIdentifierKind { BirthRegistration = 1, NationalId = 2, Passport = 3, StudentId = 4, Other = 99 }
public enum ConsentState { Pending = 1, Approved = 2, Rejected = 3, Revoked = 4, Expired = 5 }
public enum GrantState { Active = 1, Revoked = 2, Expired = 3 }
public enum EmployeeState { Active = 1, Suspended = 2, Resigned = 3, Terminated = 4, Retired = 5 }
public enum LeaveState { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4, Cancelled = 5 }
public enum PayrollRunState { Draft = 1, Calculated = 2, Approved = 3, Posted = 4, Cancelled = 5 }
public enum SalaryComponentType { Earning = 1, Deduction = 2, EmployerContribution = 3 }
public enum FeeFrequencyType { OneTime = 1, Monthly = 2, Quarterly = 3, HalfYearly = 4, Yearly = 5, PerTerm = 6 }
public enum RefundState { Draft = 1, Approved = 2, Paid = 3, Rejected = 4, Cancelled = 5 }
public enum JournalState { Draft = 1, Submitted = 2, Approved = 3, Posted = 4, Rejected = 5, Reversed = 6, Cancelled = 7 }
public enum AccountType { Asset = 1, Liability = 2, Equity = 3, Income = 4, Expense = 5 }
public enum BookCopyState { Available = 1, Issued = 2, Reserved = 3, Lost = 4, Damaged = 5, Retired = 6 }
public enum BookIssueState { Issued = 1, Returned = 2, Lost = 3, Damaged = 4 }
public enum BookReservationState { Pending = 1, Fulfilled = 2, Cancelled = 3, Expired = 4 }
public enum TransportAssignmentState { Active = 1, Closed = 2, Cancelled = 3 }
public enum HostelAllocationState { Active = 1, Closed = 2, Cancelled = 3 }
public enum CourseEnrollmentState { Active = 1, Completed = 2, Expired = 3, Cancelled = 4 }
public enum LearningTaskType { Assignment = 1, Homework = 2, Project = 3, Practice = 4 }
public enum LearningSubmissionState { Draft = 1, Submitted = 2, Graded = 3, Returned = 4 }
public enum QuizAttemptState { Started = 1, Submitted = 2, Graded = 3, Expired = 4 }
public enum NotificationChannelType { InApp = 1, Email = 2, Sms = 3, Push = 4 }
public enum DeliveryState { Pending = 1, Processing = 2, Sent = 3, Delivered = 4, Failed = 5 }
public enum FileVisibility { Private = 1, Tenant = 2, Public = 3 }
public enum CustomFieldDataType { Text = 1, Number = 2, Decimal = 3, Date = 4, DateTime = 5, Boolean = 6, Select = 7, MultiSelect = 8, Json = 9 }
public enum WebhookDeliveryState { Pending = 1, Succeeded = 2, Failed = 3, Dead = 4 }
public enum OutboxState { Pending = 1, Processing = 2, Published = 3, Failed = 4 }
