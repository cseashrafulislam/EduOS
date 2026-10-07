using AutoMapper;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;

namespace EduOS.Service.Mappings;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<AuditLog, AuditLogDto>();
        CreateMap<AuditLogFilterDto, AuditLog>();

        CreateMap<SubscriptionPlan, SubscriptionPlanDto>()
            .ForMember(d => d.Features, opt => opt.Ignore());

        CreateMap<PlanFeature, PlanFeatureDto>()
            .ForMember(d => d.FeatureCode, opt => opt.Ignore())
            .ForMember(d => d.FeatureName, opt => opt.Ignore());

        CreateMap<SubscriptionInvoice, SubscriptionInvoiceDto>()
            .ForMember(d => d.Lines, opt => opt.Ignore());
    }
}
