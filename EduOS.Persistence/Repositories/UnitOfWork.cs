using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Contexts;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduOS.Persistence.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private IDbContextTransaction? _transaction;

        public IRepository<Tenant> Tenants { get; }
        public IRepository<TenantUser> TenantUsers { get; }
        public IRepository<SubscriptionPlan> SubscriptionPlans { get; }
        public IRepository<PlanFeature> PlanFeatures { get; }
        public IRepository<TenantFeature> TenantFeatures { get; }
        public IRepository<TenantSubscription> TenantSubscriptions { get; }
        public IRepository<EmailVerificationToken> EmailVerificationTokens { get; }
        public IRepository<OnboardingProgress> OnboardingProgresses { get; }
        public IRepository<Campus> Campuses { get; private set; }
        public IRepository<AcademicYear> AcademicYears { get; private set; }
        public IRepository<AcademicTerm> AcademicTerms { get; private set; }






        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;

            Tenants = new Repository<Tenant>(_context);
            TenantUsers = new Repository<TenantUser>(_context);
            SubscriptionPlans = new Repository<SubscriptionPlan>(_context);
            PlanFeatures = new Repository<PlanFeature>(_context);
            TenantFeatures = new Repository<TenantFeature>(_context);
            TenantSubscriptions = new Repository<TenantSubscription>(_context);
            EmailVerificationTokens = new Repository<EmailVerificationToken>(_context);
            OnboardingProgresses = new Repository<OnboardingProgress>(_context);
            Campuses = new Repository<Campus>(_context);
            AcademicYears = new Repository<AcademicYear>(_context);
            AcademicTerms = new Repository<AcademicTerm>(_context);
        }

        public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();

        public async Task BeginTransactionAsync()
        {
            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction != null)
            {
                await _context.SaveChangesAsync();
                await _transaction.CommitAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _context.Dispose();
        }
    }
}