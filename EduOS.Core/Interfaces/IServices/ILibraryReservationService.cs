using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

public interface ILibraryReservationService
{
    Task<ApiResponse<BookReservationDto>> ReserveBookAsync(ReserveBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookReservationDto>> CancelReservationAsync(long reservationId, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<BookReservationDto>>> GetMyReservationsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<BookReservationDto>>> GetActiveReservationsForBookAsync(Guid bookReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookReservationDto>> FulfillReservationAsync(long reservationId, long bookCopyId, string rowVersion, CancellationToken cancellationToken = default);
}
