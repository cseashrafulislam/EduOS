using AutoMapper;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Service.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Class, ClassDto>()
                .ForMember(dest => dest.TotalSections,
                           opt => opt.MapFrom(src => src.Sections.Count))
                .ForMember(dest => dest.TotalSubjects,
                           opt => opt.MapFrom(src => src.Subjects.Count));

            CreateMap<ClassCreateDto, Class>()
               .ForMember(d => d.Id, opt => opt.Ignore())
               .ForMember(d => d.TenantId, opt => opt.Ignore());

            CreateMap<ClassUpdateDto, Class>()
                .ForMember(d => d.Id, opt => opt.Ignore())
                .ForMember(d => d.TenantId, opt => opt.Ignore());

            CreateMap<AuditLog, AuditLogDto>();
            CreateMap<AuditLogFilterDto, AuditLog>();


            // SubscriptionPlan -> SubscriptionPlanDto
            CreateMap<SubscriptionPlan, SubscriptionPlanDto>()
                .ForMember(d => d.Features,
                    opt => opt.MapFrom(s => s.PlanFeatures.Where(pf => pf.IsEnabled)));

            // PlanFeature -> PlanFeatureDto
            CreateMap<PlanFeature, PlanFeatureDto>()
                .ForMember(d => d.FeatureName,
                    opt => opt.MapFrom(s => s.Feature != null ? s.Feature.Name : string.Empty))
                .ForMember(d => d.FeatureCode,
                    opt => opt.MapFrom(s => s.Feature != null ? s.Feature.Code : string.Empty))
                .ForMember(d => d.Category,
                    opt => opt.MapFrom(s => s.Feature != null ? s.Feature.Category : null))
                .ForMember(d => d.IconName,
                    opt => opt.MapFrom(s => s.Feature != null ? s.Feature.IconName : null));

            // SubscriptionInvoice -> SubscriptionInvoiceDto
            CreateMap<SubscriptionInvoice, SubscriptionInvoiceDto>()
                .ForMember(d => d.PlanName,
                    opt => opt.MapFrom(s => s.Subscription != null && s.Subscription.SubscriptionPlan != null
                        ? s.Subscription.SubscriptionPlan.Name
                        : string.Empty));

        }
    }
}
