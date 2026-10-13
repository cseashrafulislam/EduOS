using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService : IAssetLifecycleService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AssetLifecycleService> _logger;
    public AssetLifecycleService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock, ILogger<AssetLifecycleService> logger)
    { _db=db; _user=user; _clock=clock; _logger=logger; }
}
