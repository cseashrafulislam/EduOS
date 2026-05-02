using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Persistence.Repositories
{
    public class RefreshTokenRepository : GenericRepository<RefreshToken>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(EduOSDbContext context) : base(context) { }
    }
}
