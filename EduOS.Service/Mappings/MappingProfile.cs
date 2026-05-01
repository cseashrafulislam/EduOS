using AutoMapper;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.Academic;
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

            CreateMap<ClassCreateDto, Class>();
            CreateMap<ClassUpdateDto, Class>();

            CreateMap<AuditLog, AuditLogDto>();
            CreateMap<AuditLogFilterDto, AuditLog>();

        }
    }
}
