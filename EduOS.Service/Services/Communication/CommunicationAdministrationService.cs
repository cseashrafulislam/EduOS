using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Enums.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService : ICommunicationAdministrationService
{
    private readonly IGenericRepository<CommunicationGateway> _gateways;
    private readonly IGenericRepository<MessageTemplate> _templates;
    private readonly IGenericRepository<NoticeCategory> _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _user;
    private readonly IDataProtector _protector;
    private readonly ILogger<CommunicationAdministrationService> _logger;

    public CommunicationAdministrationService(IGenericRepository<CommunicationGateway> gateways,
        IGenericRepository<MessageTemplate> templates, IGenericRepository<NoticeCategory> categories,
        IUnitOfWork unitOfWork, ICurrentUserService user, IDataProtectionProvider provider,
        ILogger<CommunicationAdministrationService> logger)
    {
        _gateways = gateways; _templates = templates; _categories = categories;
        _unitOfWork = unitOfWork; _user = user;
        _protector = provider.CreateProtector("EduOS.Communication.GatewayCredentials.v1");
        _logger = logger;
    }

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;
    private static bool ValidChannel(NotificationChannelType channel) => Enum.IsDefined(channel);
    private static bool MatchesVersion(byte[] version, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return version.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }
    private static CommunicationGatewayDto Map(CommunicationGateway row) => new()
    {
        Id = row.Id, ProviderCode = row.ProviderCode, Channel = row.Channel, Endpoint = row.Endpoint,
        HasCredential = !string.IsNullOrEmpty(row.ProtectedCredential), IsDefault = row.IsDefault,
        IsActive = row.IsActive, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static MessageTemplateDto Map(MessageTemplate row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name, Channel = row.Channel,
        SubjectTemplate = row.SubjectTemplate, BodyTemplate = row.BodyTemplate, IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static NoticeCategoryDto Map(NoticeCategory row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, IsActive = row.IsActive,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
