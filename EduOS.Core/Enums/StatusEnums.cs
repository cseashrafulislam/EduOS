namespace EduOS.Core.Enums
{
    public static class AttendanceStatus
    {
        public const string Present = "Present";
        public const string Absent = "Absent";
        public const string Late = "Late";
        public const string Leave = "Leave";
        public const string Holiday = "Holiday";
    }

    public static class STPaymentStatus
    {
        public const string Paid = "Paid";
        public const string Partial = "Partial";
        public const string Unpaid = "Unpaid";
        public const string Refunded = "Refunded";
    }

    public static class STPaymentMethod
    {
        public const string Cash = "Cash";
        public const string Bkash = "Bkash";
        public const string Nagad = "Nagad";
        public const string Rocket = "Rocket";
        public const string Card = "Card";
        public const string Bank = "Bank";
        public const string Cheque = "Cheque";
    }

    public static class ApprovalStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public static class StudentStatus
    {
        public const string Active = "Active";
        public const string TC = "TC";
        public const string Passed = "Passed";
        public const string Dropout = "Dropout";
    }

    public static class NotificationType
    {
        public const string SMS = "SMS";
        public const string Email = "Email";
        public const string Push = "Push";
        public const string InApp = "InApp";
    }

    public static class DaysOfWeek
    {
        public const string Saturday = "Saturday";
        public const string Sunday = "Sunday";
        public const string Monday = "Monday";
        public const string Tuesday = "Tuesday";
        public const string Wednesday = "Wednesday";
        public const string Thursday = "Thursday";
        public const string Friday = "Friday";
    }
}
