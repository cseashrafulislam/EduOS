using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Enums
{
    public enum InvoiceStatus
    {
        Pending = 1,
        Partial = 2,
        Paid = 3,
        Cancelled = 4,
        Overdue = 5
    }
}
