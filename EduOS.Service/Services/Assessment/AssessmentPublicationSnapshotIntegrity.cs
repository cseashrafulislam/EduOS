namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Fail closed if a finalized publication has missing, deleted, invalid or duplicate enrollment evidence.</summary>
public static class AssessmentPublicationSnapshotIntegrity
{
    public static bool IsReconciled(int total, int active, int valid, int distinctEnrollments) =>
        total > 0 && total == active && active == valid && valid == distinctEnrollments;
}
