using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        Task<int> SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();




        IRepository<Tenant> Tenants { get; }
        IRepository<TenantUser> TenantUsers { get; }
        IRepository<SubscriptionPlan> SubscriptionPlans { get; }
        IRepository<PlanFeature> PlanFeatures { get; }
        IRepository<TenantFeature> TenantFeatures { get; }
        IRepository<TenantSubscription> TenantSubscriptions { get; }
        IRepository<EmailVerificationToken> EmailVerificationTokens { get; }
        IRepository<OnboardingProgress> OnboardingProgresses { get; }
        IRepository<Campus> Campuses { get; }
        IRepository<AcademicYear> AcademicYears { get; }
        IRepository<AcademicTerm> AcademicTerms { get; }
    }
}
