using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.LMS;

public sealed class LmsLearnerCourseProgressDto
{
    public long CourseEnrollmentId { get; set; }
    public Guid CourseReference { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public CourseEnrollmentState State { get; set; }
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RequiredLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int RequiredAssignments { get; set; }
    public int GradedAssignments { get; set; }
    public int RequiredQuizzes { get; set; }
    public int PassedQuizzes { get; set; }
    // Lesson completion is not a course-completion certificate or a weighted grade.
    public decimal LessonCompletionPercent { get; set; }
}
