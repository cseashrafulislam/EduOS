using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using Xunit;

namespace EduOS.Tests.Persistence;

public class LongIdBoundaryTests
{
    [Fact]
    public void Entity_foreign_key_properties_do_not_use_32_bit_identifiers()
    {
        var offenders = typeof(ClassRoutine).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.StartsWith("EduOS.Core.Entities", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties().Select(p => new { Type = t, Property = p }))
            .Where(x => x.Property.Name != "Id" && x.Property.Name.EndsWith("Id", StringComparison.Ordinal))
            .Where(x => x.Property.PropertyType == typeof(int) || x.Property.PropertyType == typeof(int?))
            .Select(x => $"{x.Type.FullName}.{x.Property.Name}: {x.Property.PropertyType.Name}")
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "32-bit entity identifier properties remain:\n" + string.Join("\n", offenders));
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
