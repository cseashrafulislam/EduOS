using EduOS.Service.Services.Finance;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationAutomaticInvoicePostingTests
{
    [Fact]
    public void Posting_key_is_stable_per_tenant_kind_and_clearance()
    {
        var key = ReservationAutomaticInvoicePosting.InvoiceKey(11, "HOSTEL", 701);
        Assert.Equal(key, ReservationAutomaticInvoicePosting.InvoiceKey(11, "HOSTEL", 701));
        Assert.NotEqual(key, ReservationAutomaticInvoicePosting.InvoiceKey(11, "TRANSPORT", 701));
        Assert.NotEqual(key, ReservationAutomaticInvoicePosting.InvoiceKey(12, "HOSTEL", 701));
        Assert.NotEqual(key, ReservationAutomaticInvoicePosting.InvoiceKey(11, "HOSTEL", 702));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReservationAutomaticInvoicePosting.InvoiceKey(11, "INVALID", 701));
    }
}
