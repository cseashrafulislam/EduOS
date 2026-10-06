using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;

namespace EduOS.Persistence.Repositories;

public class TenantUserRepository : GenericRepository<TenantMembership>, ITenantUserRepository
{
    public TenantUserRepository(EduOSDbContext context) : base(context) { }
}
