using EduOS.Core.DTOs.Auth;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.DTOs.SaaS;
using EduOS.Service.Helpers;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace EduOS.Tests.Services;

public class AuthIdentityContractTests
{
    [Theory]
    [InlineData(typeof(OnboardingStatusDto), nameof(OnboardingStatusDto.TenantId), typeof(long))]
    [InlineData(typeof(UserSummaryDto), nameof(UserSummaryDto.Id), typeof(long))]
    [InlineData(typeof(TenantMembershipDto), nameof(TenantMembershipDto.UserId), typeof(long))]
    [InlineData(typeof(User), nameof(User.TenantId), typeof(long))]
    public void Auth_identity_contracts_use_long_ids(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

        property.Should().NotBeNull($"{type.Name}.{propertyName} is part of the persisted identity contract");
        property!.PropertyType.Should().Be(expectedType,
            $"{type.Name}.{propertyName} must remain compatible with bigint-backed identifiers");
    }

    [Fact]
    public void Public_institution_signup_uses_opaque_references_not_internal_ids()
    {
        typeof(InstitutionSignupResponseDto).GetProperty(nameof(InstitutionSignupResponseDto.TenantReference))!
            .PropertyType.Should().Be(typeof(Guid));
        typeof(InstitutionSignupResponseDto).GetProperty(nameof(InstitutionSignupResponseDto.UserReference))!
            .PropertyType.Should().Be(typeof(Guid));
    }
}
