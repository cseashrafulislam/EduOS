using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class LongIdBoundaryTests
{
    [Fact]
    public void Mapped_identifier_properties_do_not_use_32_bit_or_legacy_shadow_boundaries()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"mapped-id-audit-{Guid.NewGuid():N}").Options);

        var offenders = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => new { Entity = e, Property = p }))
            .Where(x => (x.Property.Name != "Id" && x.Property.Name.EndsWith("Id", StringComparison.Ordinal) &&
                         (x.Property.ClrType == typeof(int) || x.Property.ClrType == typeof(int?))) ||
                        (x.Property.PropertyInfo == null && x.Property.FieldInfo == null &&
                         x.Property.Name.EndsWith("Id1", StringComparison.Ordinal)))
            .Select(x => $"{x.Entity.ClrType.FullName}.{x.Property.Name}: {x.Property.ClrType.Name}")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Mapped 32-bit or legacy shadow identifier properties remain:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Repository_identifier_parameters_do_not_use_32_bit_identifiers()
    {
        var offenders = typeof(ITenantSubscriptionRepository).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "EduOS.Core.Interfaces.IRepositories")
            .SelectMany(t => t.GetMethods().SelectMany(m => m.GetParameters()
                .Select(p => new { Type = t, Method = m, Parameter = p })))
            .Where(x => x.Parameter.Name?.EndsWith("Id", StringComparison.OrdinalIgnoreCase) == true)
            .Where(x => x.Parameter.ParameterType == typeof(int) || x.Parameter.ParameterType == typeof(int?))
            .Select(x => $"{x.Type.Name}.{x.Method.Name}({x.Parameter.Name}: {x.Parameter.ParameterType.Name})")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "32-bit repository identifier parameters remain:\n" + string.Join("\n", offenders));
    }
}
