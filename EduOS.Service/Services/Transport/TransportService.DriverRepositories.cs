using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.Extensions.Logging;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    private IGenericRepository<TransportDriver> _drivers = null!;
    private IGenericRepository<VehicleDriverAssignment> _driverLinks = null!;

    // The existing constructor remains intact for legacy consumers. The registered
    // open-generic repositories allow DI to select this more complete constructor.
    public TransportService(IGenericRepository<TransportRoute> routes, IGenericRepository<RouteStop> stops,
        IGenericRepository<Vehicle> vehicles, IGenericRepository<RouteVehicleAssignment> vehicleRoutes,
        IGenericRepository<StudentTransport> assignments, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Student> students, IGenericRepository<UserCampusAccess> campusAccesses,
        IGenericRepository<Campus> campuses, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        TimeProvider clock, ILogger<TransportService> logger,
        IGenericRepository<TransportDriver> drivers, IGenericRepository<VehicleDriverAssignment> driverLinks,
        IReservationFinancePolicy reservationFinancePolicy,
        IReservationCommandReceiptStore commandReceipts)
        : this(routes, stops, vehicles, vehicleRoutes, assignments, enrollments, students,
            campusAccesses, campuses, unitOfWork, currentUser, clock, logger, reservationFinancePolicy, commandReceipts)
    {
        _drivers = drivers;
        _driverLinks = driverLinks;
    }
}
