using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduOS.Core.Entities.Academic
{
    public class Subject : BaseTenantEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ShortName { get; set; }

        public SubjectType SubjectType { get; set; } = SubjectType.Core;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultCreditHours { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultFullMarks { get; set; } = 100;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultPassMarks { get; set; } = 33;

        public bool HasPractical { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public enum SubjectType
    {
        Core = 1,
        Elective = 2,
        Optional = 3,
        Practical = 4,
        Lab = 5
    }
}