using System;
using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Teacher : TenantEntity
    {
        public string TeacherCode { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public DateTime? JoinDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
