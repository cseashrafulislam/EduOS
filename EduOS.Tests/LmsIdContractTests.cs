using EduOS.Core.Entities.LMS;
using Xunit;

namespace EduOS.Tests;

public sealed class LmsIdContractTests
{
    [Theory]
    [InlineData(typeof(Quiz), nameof(Quiz.Id), typeof(long))]
    [InlineData(typeof(Quiz), nameof(Quiz.TenantId), typeof(long))]
    [InlineData(typeof(Quiz), nameof(Quiz.CourseId), typeof(long))]
    [InlineData(typeof(QuizResult), nameof(QuizResult.Id), typeof(long))]
    [InlineData(typeof(QuizResult), nameof(QuizResult.TenantId), typeof(long))]
    [InlineData(typeof(QuizResult), nameof(QuizResult.QuizId), typeof(long))]
    [InlineData(typeof(QuizResult), nameof(QuizResult.StudentId), typeof(long))]
    public void Quiz_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
