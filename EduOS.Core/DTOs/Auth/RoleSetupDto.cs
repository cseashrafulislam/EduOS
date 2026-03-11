using System.Collections.Generic;

namespace EduOS.Core.DTOs.Auth
{
    public class RoleSetupDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }



    public class RolePermissionSaveDto
    {
        public int RoleId { get; set; }
        public List<RolePermissionItemDto> Permissions { get; set; } = new();
    }

    public class RolePermissionItemDto
    {
        public int AppPageId { get; set; }
        public int PermissionId { get; set; }
        public bool IsAllowed { get; set; }
    }

}