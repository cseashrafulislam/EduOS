using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduOS.Tests.Architecture;

public sealed class ApiServiceRegistrationTests
{
    private static readonly string[] CanonicalControllers =
    [
        "AccountingJournalsController",
        "AccountingReportsController",
        "AccountingSetupController",
        "AssetLifecycleController",
        "EmployeeAttendanceController",
        "FeeHeadAccountingController",
        "FeeReceivablesAgingController",
        "FinanceAdjustmentsController",
        "GuardianCommunicationPreferencesController",
        "HrSetupController",
        "InventoryCatalogController",
        "InventoryMovementsController",
        "LeaveEntitlementsController",
        "LibraryBranchesController",
        "LibraryReservationsController",
        "LmsAssessmentController",
        "LmsAssignmentAttachmentsController",
        "LmsCourseContentController",
        "LmsLiveClassesController",
        "LoanDisbursementController",
        "LoanRecoveryController",
        "PayrollJournalPostingController",
        "PayrollPaymentPostingController",
        "StudentGuardiansController",
        "StudentInvoicePostingController",
        "StudentOptionalSubjectsController",
        "StudentPaymentPostingController",
        "StudentTransferRequestsController",
        "SubjectPrerequisitesController",
        "TenantFeatureAuditApiController",
        "TenantFeaturesApiController"
    ];

    [Fact]
    public void Migrated_api_controllers_require_only_one_registered_scoped_contract()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices(new ConfigurationBuilder().Build());
        var assembly = typeof(AccountingJournalsController).Assembly;

        foreach (var controllerName in CanonicalControllers)
        {
            var controller = assembly.GetType($"EduOS.App.Controllers.Api.{controllerName}");
            Assert.NotNull(controller);
            Assert.True(typeof(ControllerBase).IsAssignableFrom(controller), controllerName);

            var constructor = Assert.Single(controller.GetConstructors());
            var parameter = Assert.Single(constructor.GetParameters());
            var contract = parameter.ParameterType;
            Assert.True(contract.IsInterface && contract.Namespace == typeof(IFinanceAdjustmentService).Namespace,
                $"{controllerName} must directly inject an application service contract.");

            var registration = Assert.Single(services.Where(x => x.ServiceType == contract));
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.NotNull(registration.ImplementationType);
            Assert.True(contract.IsAssignableFrom(registration.ImplementationType),
                $"{controllerName} maps {contract.Name} to an incompatible implementation.");
        }
    }

    [Fact]
    public void Api_controller_application_service_dependencies_have_canonical_registration()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices(new ConfigurationBuilder().Build());
        var contracts = typeof(AccountingJournalsController).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && x.Namespace == "EduOS.App.Controllers.Api"
                && typeof(ControllerBase).IsAssignableFrom(x))
            .SelectMany(x => x.GetConstructors().SelectMany(c => c.GetParameters()))
            .Select(x => x.ParameterType)
            .Where(x => x.IsInterface && x.Namespace == typeof(IFinanceAdjustmentService).Namespace)
            .Distinct()
            .OrderBy(x => x.Name)
            .ToArray();

        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            var registration = Assert.Single(services.Where(x => x.ServiceType == contract));
            Assert.True(registration.Lifetime is ServiceLifetime.Scoped or ServiceLifetime.Singleton,
                $"{contract.Name} has an unexpected lifetime: {registration.Lifetime}.");
        }
    }
}
