using System;
using System.Linq;
using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryReservationCsvTests
{
    [Fact]
    public void Export_is_bounded_utf8_and_does_not_include_private_borrower_identifiers()
    {
        var row = new BookReservationDto {
            BookTitle = "  =HYPERLINK(\"https://invalid.example\")",
            StudentName = "Private borrower", StudentReference = Guid.NewGuid(),
            Id = 1234, RowVersion = "private-rowversion",
            State = BookReservationState.Pending,
            ReservedAt = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc)
        };
        var csv = Encoding.UTF8.GetString(LibraryReservationCsvWriter.Write([row], false));
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("'  =HYPERLINK", csv);
        Assert.DoesNotContain("Private borrower", csv);
        Assert.DoesNotContain(row.StudentReference.ToString(), csv);
        Assert.DoesNotContain("private-rowversion", csv);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LibraryReservationCsvWriter.Write(Enumerable.Repeat(row, 101).ToArray(), false));
    }

    [Fact]
    public void Export_endpoint_requires_student_role()
    {
        var action = typeof(LibraryReservationsController).GetMethod(nameof(LibraryReservationsController.ExportMine))!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), attribute => attribute.Roles == "Student");
    }
}
