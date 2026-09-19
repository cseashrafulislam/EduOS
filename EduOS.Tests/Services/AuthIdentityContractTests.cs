using EduOS.Core.DTOs.Auth;
using EduOS.Service.Helpers;
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace EduOS.Tests.Services;

public class AuthIdentityContractTests
{
    [Theory]
    [InlineData(typeof(SignupResponseDto), nameof(SignupResponseDto.TenantId), typeof(long?))]
    [InlineData(typeof(SignupResponseDto), nameof(SignupResponseDto.UserId), typeof(long?))]
    [InlineData(typeof(User), nameof(User.TenantId), typeof(long))]
    public void Auth_identity_contracts_use_long_ids(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

        property.Should().NotBeNull($"{type.Name}.{propertyName} is part of the persisted identity contract");
        property!.PropertyType.Should().Be(expectedType,
            $"{type.Name}.{propertyName} must remain compatible with bigint-backed identifiers");
    }
}
