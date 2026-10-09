using EduOS.Core.Entities.LMS;
using Xunit;

namespace EduOS.Tests;

public sealed class LmsIdContractTests
{
    [Theory]
    [InlineData(typeof(Quiz), nameof(Quiz.Id), typeof(long))]
    [InlineData(typeof(Quiz), nameof(Quiz.TenantId), typeof(long))]
    [InlineData(typeof(Quiz), nameof(Quiz.CourseId), typeof(long))]
    [InlineData(typeof(QuizAttempt), nameof(QuizAttempt.Id), typeof(long))]
    [InlineData(typeof(QuizAttempt), nameof(QuizAttempt.TenantId), typeof(long))]
    [InlineData(typeof(QuizAttempt), nameof(QuizAttempt.QuizId), typeof(long))]
    [InlineData(typeof(QuizAttempt), nameof(QuizAttempt.CourseEnrollmentId), typeof(long))]
    public void Quiz_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
