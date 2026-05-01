namespace EduOS.Core.Constants
{
    public static class PermissionConstants
    {
        public static class Tenant
        {
            public const string View = "tenant.view";
            public const string Manage = "tenant.manage";
        }

        public static class User
        {
            public const string View = "user.view";
            public const string Create = "user.create";
            public const string Edit = "user.edit";
            public const string Delete = "user.delete";
        }

        public static class Class
        {
            public const string View = "class.view";
            public const string Create = "class.create";
            public const string Edit = "class.edit";
            public const string Delete = "class.delete";
        }

        public static class Student
        {
            public const string View = "student.view";
            public const string Create = "student.create";
            public const string Edit = "student.edit";
            public const string Delete = "student.delete";
            public const string Promote = "student.promote";
        }

        public static class Teacher
        {
            public const string View = "teacher.view";
            public const string Create = "teacher.create";
            public const string Edit = "teacher.edit";
            public const string Delete = "teacher.delete";
        }

        public static class Attendance
        {
            public const string View = "attendance.view";
            public const string Mark = "attendance.mark";
            public const string Edit = "attendance.edit";
        }

        public static class Exam
        {
            public const string View = "exam.view";
            public const string Create = "exam.create";
            public const string Edit = "exam.edit";
            public const string Delete = "exam.delete";
            public const string Publish = "exam.publish";
        }

        public static class Mark
        {
            public const string View = "mark.view";
            public const string Entry = "mark.entry";
            public const string Edit = "mark.edit";
        }

        public static class Fee
        {
            public const string View = "fee.view";
            public const string Collect = "fee.collect";
            public const string Refund = "fee.refund";
            public const string Discount = "fee.discount";
        }

        public static class Payroll
        {
            public const string View = "payroll.view";
            public const string Process = "payroll.process";
            public const string Approve = "payroll.approve";
        }

        public static class Library
        {
            public const string View = "library.view";
            public const string Issue = "library.issue";
            public const string Return = "library.return";
        }

        public static class Notice
        {
            public const string View = "notice.view";
            public const string Create = "notice.create";
            public const string Edit = "notice.edit";
            public const string Delete = "notice.delete";
        }

        public static class Report
        {
            public const string View = "report.view";
            public const string Export = "report.export";
        }
    }
}
