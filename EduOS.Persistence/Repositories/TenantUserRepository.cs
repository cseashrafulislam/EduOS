using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Persistence.Repositories
{
    public class TenantUserRepository : GenericRepository<TenantUser>, ITenantUserRepository
    {
        public TenantUserRepository(EduOSDbContext context) : base(context) { }
    }
}
