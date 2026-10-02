using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.DTOs.Library;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class ReservationService
{
    private readonly AppDbContext _context;

    public ReservationService(AppDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // ACTIVE STATUS CHECK
    //
    // Automatic na ang reservation ("Reserved" agad).
    // Pending / Approved ay tinatanggap pa rin para sa
    // lumang rows.
    // =========================================================
    private static bool IsActiveStatus(string? status)
    {
        return status == "Reserved" ||
               status == "Pending" ||
               status == "Approved";
    }

    // =========================================================
    // RESERVE BOOK
    // POST: api/library/reservations
    // =========================================================
    public async Task<ServiceResponse<ReservationDto>> ReserveBookAsync(
        int userId,
        int bookId)
    {
        var response = new ServiceResponse<ReservationDto>();

        var book = await _context.Books
            .FirstOrDefaultAsync(b => b.BookId == bookId);

        if (book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // =====================================================
        // CHECK EXISTING ACTIVE RESERVATION
        // =====================================================
        bool alreadyReserved = await _context.Reservations.AnyAsync(r =>
            r.UserId == userId &&
            r.BookId == bookId &&
            (
                r.Status == "Reserved" ||
                r.Status == "Pending" ||
                r.Status == "Approved"
            ));

        if (alreadyReserved)
        {
            response.Success = false;
            response.Message =
                "You already have an active reservation for this book.";

            return response;
        }

        // =====================================================
        // CHECK IF USER ALREADY BORROWED THIS BOOK
        // =====================================================
        bool alreadyBorrowed = await _context.BorrowTransactions.AnyAsync(b =>
            b.UserId == userId &&
            b.BookId == bookId &&
            b.Status == "Borrowed");

        if (alreadyBorrowed)
        {
            response.Success = false;
            response.Message = "You already borrowed this book.";
            return response;
        }

        // =====================================================
        // CREATE RESERVATION
        // NO APPROVAL REQUIRED
        // =====================================================

        var now = DateTime.Now;

        var reservation = new Reservation
        {
            UserId = userId,
            BookId = bookId,

            ReservationAt = now,

            // Reservation is valid for 3 days
            ExpirationDate = now.AddDays(3),

            // Immediately active. No admin approval.
            Status = "Reserved",

            Remarks = "",
            CreatedAt = now
        };

        _context.Reservations.Add(reservation);

        // =====================================================
        // LIBRARY ACTIVITY
        // =====================================================

        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = userId,
            BookId = bookId,
            ActivityType = "Reserve",
            Title = "Book Reserved",
            Description = $"You reserved '{book.Title}'.",
            CreatedAt = now
        });

        // =====================================================
        // LIBRARY NOTIFICATION
        // =====================================================

        _context.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = userId,
            Type = "reservation-created",
            Title = "Book Reserved",
            Message =
                $"Your reservation for '{book.Title}' has been created successfully.",
            IsRead = false,
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        // =====================================================
        // RESPONSE
        // =====================================================

        response.Success = true;
        response.Message = "Book reserved successfully.";

        response.Data = new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            UserId = reservation.UserId,
            BookId = reservation.BookId,

            BookTitle = book.Title,
            Author = book.Author,
            CoverImage = book.CoverImage,

            ReservationAt = reservation.ReservationAt,
            ExpirationDate = reservation.ExpirationDate,

            Status = reservation.Status,
            Remarks = reservation.Remarks
        };

        return response;
    }


    // =========================================================
    // GET MY RESERVATIONS
    // GET: api/library/reservations/student/{userId}
    // =========================================================
    public async Task<List<ReservationDto>> GetMyReservationsAsync(
        int userId)
    {
        return await _context.Reservations
            .Include(r => r.Book)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.ReservationAt)
            .Select(r => new ReservationDto
            {
                ReservationId = r.ReservationId,

                UserId = r.UserId,
                BookId = r.BookId,

                BookTitle = r.Book!.Title,
                Author = r.Book.Author,
                CoverImage = r.Book.CoverImage,

                ReservationAt = r.ReservationAt,
                ExpirationDate = r.ExpirationDate,

                Status = r.Status,
                Remarks = r.Remarks
            })
            .ToListAsync();
    }


    // =========================================================
    // CANCEL RESERVATION
    // PUT: api/library/reservations/cancel/{reservationId}
    // =========================================================
    public async Task<ServiceResponse<ReservationDto>>
        CancelReservationAsync(int reservationId)
    {
        var response = new ServiceResponse<ReservationDto>();

        var reservation = await _context.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r =>
                r.ReservationId == reservationId);

        if (reservation == null)
        {
            response.Success = false;
            response.Message = "Reservation not found.";
            return response;
        }

        // Only active reservations can be cancelled
        if (!IsActiveStatus(reservation.Status))
        {
            response.Success = false;
            response.Message =
                "Only active reservations can be cancelled.";

            return response;
        }

        if (reservation.Book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // =====================================================
        // UPDATE STATUS
        // =====================================================

        reservation.Status = "Cancelled";

        var now = DateTime.Now;

        // =====================================================
        // ACTIVITY
        // =====================================================

        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = reservation.UserId,
            BookId = reservation.BookId,

            ActivityType = "CancelReservation",
            Title = "Reservation Cancelled",

            Description =
                $"You cancelled your reservation for '{reservation.Book.Title}'.",

            CreatedAt = now
        });

        // =====================================================
        // NOTIFICATION
        // =====================================================

        _context.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = reservation.UserId,

            Type = "reservation-cancelled",
            Title = "Reservation Cancelled",

            Message =
                $"Your reservation for '{reservation.Book.Title}' has been cancelled.",

            IsRead = false,
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        response.Success = true;
        response.Message = "Reservation cancelled successfully.";

        response.Data = new ReservationDto
        {
            ReservationId = reservation.ReservationId,

            UserId = reservation.UserId,
            BookId = reservation.BookId,

            BookTitle = reservation.Book.Title,
            Author = reservation.Book.Author,
            CoverImage = reservation.Book.CoverImage,

            ReservationAt = reservation.ReservationAt,
            ExpirationDate = reservation.ExpirationDate,

            Status = reservation.Status,
            Remarks = reservation.Remarks
        };

        return response;
    }


    // =========================================================
    // EXPIRE RESERVATIONS
    // =========================================================
    public async Task<int> ExpireReservationsAsync()
    {
        var now = DateTime.Now;

        var expiredReservations = await _context.Reservations
            .Where(r =>
                (
                    r.Status == "Reserved" ||
                    r.Status == "Pending" ||
                    r.Status == "Approved"
                ) &&
                r.ExpirationDate < now)
            .ToListAsync();

        if (!expiredReservations.Any())
            return 0;

        foreach (var reservation in expiredReservations)
        {
            reservation.Status = "Expired";
        }

        await _context.SaveChangesAsync();

        return expiredReservations.Count;
    }


    // =========================================================
    // CLAIM RESERVED BOOK
    //
    // Staff scanner ang gagamit nito.
    //
    // Reserved -> Claimed
    // Reservation -> BorrowTransaction
    // =========================================================
    public async Task<ServiceResponse<BorrowTransactionDto>>
        ClaimReservedBookAsync(int reservationId)
    {
        var response =
            new ServiceResponse<BorrowTransactionDto>();

        var reservation = await _context.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r =>
                r.ReservationId == reservationId);

        if (reservation == null)
        {
            response.Success = false;
            response.Message = "Reservation not found.";
            return response;
        }

        // =====================================================
        // ONLY ACTIVE RESERVATIONS CAN BE CLAIMED
        // =====================================================

        if (!IsActiveStatus(reservation.Status))
        {
            response.Success = false;
            response.Message =
                "Only active reservations can be claimed.";

            return response;
        }

        if (reservation.Book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // =====================================================
        // CHECK EXPIRATION
        // =====================================================

        if (reservation.ExpirationDate < DateTime.Now)
        {
            reservation.Status = "Expired";

            await _context.SaveChangesAsync();

            response.Success = false;
            response.Message =
                "This reservation has already expired.";

            return response;
        }

        // =====================================================
        // CHECK BOOK AVAILABILITY
        // =====================================================

        if (reservation.Book.AvailableCopies <= 0)
        {
            response.Success = false;
            response.Message =
                "No available copies for this book.";

            return response;
        }

        // =====================================================
        // PREVENT DUPLICATE ACTIVE BORROW
        // =====================================================

        bool alreadyBorrowed =
            await _context.BorrowTransactions.AnyAsync(x =>
                x.UserId == reservation.UserId &&
                x.BookId == reservation.BookId &&
                x.Status == "Borrowed");

        if (alreadyBorrowed)
        {
            response.Success = false;
            response.Message =
                "Student already borrowed this book.";

            return response;
        }

        // =====================================================
        // MAXIMUM 3 ACTIVE BOOKS
        // =====================================================

        int activeBorrowCount =
            await _context.BorrowTransactions.CountAsync(x =>
                x.UserId == reservation.UserId &&
                x.Status == "Borrowed");

        if (activeBorrowCount >= 3)
        {
            response.Success = false;
            response.Message =
                "Student has reached the maximum borrowing limit.";

            return response;
        }

        // =====================================================
        // CREATE BORROW TRANSACTION
        // =====================================================

        var now = DateTime.Now;

        var borrow = new BorrowTransaction
        {
            UserId = reservation.UserId,
            BookId = reservation.BookId,

            BorrowDate = now,
            DueDate = now.AddDays(7),

            ReturnDate = null,

            RenewalCount = 0,

            Status = "Borrowed",

            Fine = 0,

            Remarks = reservation.Remarks,

            CreatedAt = now
        };

        _context.BorrowTransactions.Add(borrow);

        // =====================================================
        // UPDATE BOOK
        // =====================================================

        reservation.Book.AvailableCopies--;

        // =====================================================
        // RESERVATION IS NOW CLAIMED
        // =====================================================

        reservation.Status = "Claimed";

        // =====================================================
        // ACTIVITY
        // =====================================================

        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = reservation.UserId,
            BookId = reservation.BookId,

            ActivityType = "ClaimReservation",
            Title = "Book Claimed",

            Description =
                $"You claimed '{reservation.Book.Title}'.",

            CreatedAt = now
        });

        // =====================================================
        // NOTIFICATION
        // =====================================================

        _context.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = reservation.UserId,

            Type = "reservation-claimed",
            Title = "Book Claimed",

            Message =
                $"Your reserved book '{reservation.Book.Title}' has been claimed successfully.",

            IsRead = false,
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        // =====================================================
        // RESPONSE
        // =====================================================

        response.Success = true;
        response.Message = "Book claimed successfully.";

        response.Data = new BorrowTransactionDto
        {
            BorrowId = borrow.BorrowId,

            UserId = borrow.UserId,
            BookId = borrow.BookId,

            BookTitle = reservation.Book.Title,
            Author = reservation.Book.Author,
            CoverImage = reservation.Book.CoverImage,

            BorrowDate = borrow.BorrowDate,
            DueDate = borrow.DueDate,
            ReturnDate = borrow.ReturnDate,

            RenewalCount = borrow.RenewalCount,

            Status = borrow.Status,

            Fine = borrow.Fine,
            Remarks = borrow.Remarks
        };

        return response;
    }
}