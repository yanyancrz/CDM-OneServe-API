    using CDM_OneServe_API.Data;
    using CDM_OneServe_API.DTOs;
    using CDM_OneServe_API.Models;
    using Microsoft.EntityFrameworkCore;

    namespace CDM_OneServe_API.Services;

    public class ReservationService
    {
        private readonly AppDbContext _context;
        public ReservationService(AppDbContext context)
        {
            _context = context;
        }


        public async Task<ServiceResponse<ReservationDto>> ReserveBookAsync(int userId, int bookId)
        {
            var response = new ServiceResponse<ReservationDto>();

            var book = await _context.Books.FindAsync(bookId);

            if (book == null)
            {
                response.Success = false;
                response.Message = "Book not found.";
                return response;
            }

            // Already reserved
            bool alreadyReserved = await _context.Reservations.AnyAsync(r =>
                r.UserId == userId &&
                r.BookId == bookId &&
                r.Status == "Pending");

            if (alreadyReserved)
            {
                response.Success = false;
                response.Message = "You already have a pending reservation for this book.";
                return response;
            }

            // Already borrowed
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

            var reservation = new Reservation
            {
                UserId = userId,
                BookId = bookId,
                ReservationAt = DateTime.Now,
                ExpirationDate = DateTime.Now.AddDays(3),
                Status = "Pending",
                Remarks = "",
                CreatedAt = DateTime.Now
            };

        _context.Reservations.Add(reservation);

        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = userId,
            BookId = bookId,
            ActivityType = "Reserve",
            Title = "Book Reserved",
            Description = $"You reserved '{book.Title}'.",
            CreatedAt = DateTime.Now
        });

        await _context.SaveChangesAsync();

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
                ReservationAt = DateTime.Now,
                ExpirationDate = reservation.ExpirationDate,
                Status = reservation.Status,
                Remarks = reservation.Remarks
            };

            return response;
        }

        // ============================
        // My Reservations
        // ============================
        public async Task<List<ReservationDto>> GetMyReservationsAsync(int userId)
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

    // ============================
    // Cancel Reservation
    // ============================
    public async Task<ServiceResponse<ReservationDto>> CancelReservationAsync(int reservationId)
    {
        var response = new ServiceResponse<ReservationDto>();

        var reservation = await _context.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

        if (reservation == null)
        {
            response.Success = false;
            response.Message = "Reservation not found.";
            return response;
        }

        if (reservation.Status != "Pending")
        {
            response.Success = false;
            response.Message = "Only pending reservations can be cancelled.";
            return response;
        }

        if (reservation.Book == null)
        {
            response.Success = false;
            response.Message = "Book not found.";
            return response;
        }

        // Update reservation status
        reservation.Status = "Cancelled";

        // Add to Recent Activities
        _context.LibraryActivities.Add(new LibraryActivity
        {
            UserId = reservation.UserId,
            BookId = reservation.BookId,
            ActivityType = "CancelReservation",
            Title = "Reservation Cancelled",
            Description = $"You cancelled your reservation for '{reservation.Book.Title}'.",
            CreatedAt = DateTime.Now
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

    // ============================
    // Approve Reservation
    // ============================
    public async Task<ServiceResponse<ReservationDto>> ApproveReservationAsync(int reservationId)
        {
            var response = new ServiceResponse<ReservationDto>();

            var reservation = await _context.Reservations
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null)
            {
                response.Success = false;
                response.Message = "Reservation not found.";
                return response;
            }

            if (reservation.Status != "Pending")
            {
                response.Success = false;
                response.Message = "Only pending reservations can be approved.";
                return response;
            }

            if (reservation.Book == null)
            {
                response.Success = false;
                response.Message = "Book not found.";
                return response;
            }

            if (reservation.Book.AvailableCopies <= 0)
            {
                response.Success = false;
                response.Message = "No available copies for this book.";
                return response;
            }

            reservation.Status = "Approved";

            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message = "Reservation approved successfully.";

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

        // ============================
        // Reject Reservation
        // ============================
        public async Task<ServiceResponse<ReservationDto>> RejectReservationAsync(int reservationId)
        {
            var response = new ServiceResponse<ReservationDto>();

            var reservation = await _context.Reservations
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null)
            {
                response.Success = false;
                response.Message = "Reservation not found.";
                return response;
            }

            if (reservation.Status != "Pending")
            {
                response.Success = false;
                response.Message = "Only pending reservations can be rejected.";
                return response;
            }

            reservation.Status = "Rejected";

            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message = "Reservation rejected successfully.";

            response.Data = new ReservationDto
            {
                ReservationId = reservation.ReservationId,
                UserId = reservation.UserId,
                BookId = reservation.BookId,
                BookTitle = reservation.Book!.Title,
                Author = reservation.Book.Author,
                CoverImage = reservation.Book.CoverImage,
                ReservationAt = reservation.ReservationAt,
                ExpirationDate = reservation.ExpirationDate,
                Status = reservation.Status,
                Remarks = reservation.Remarks
            };

            return response;
        }

        // ============================
        // Auto Expire Reservations
        // ============================
        public async Task<int> ExpireReservationsAsync()
        {
            var expiredReservations = await _context.Reservations
                .Where(r =>
                    (r.Status == "Pending" || r.Status == "Approved") &&
                    r.ExpirationDate < DateTime.Now)
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

        // ============================
        // Claim Reserved Book
        // ============================
        public async Task<ServiceResponse<BorrowTransactionDto>> ClaimReservedBookAsync(int reservationId)
        {
            var response = new ServiceResponse<BorrowTransactionDto>();

            var reservation = await _context.Reservations
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null)
            {
                response.Success = false;
                response.Message = "Reservation not found.";
                return response;
            }

            if (reservation.Status != "Approved")
            {
                response.Success = false;
                response.Message = "Only approved reservations can be claimed.";
                return response;
            }

            if (reservation.Book == null)
            {
                response.Success = false;
                response.Message = "Book not found.";
                return response;
            }

            if (reservation.Book.AvailableCopies <= 0)
            {
                response.Success = false;
                response.Message = "No available copies.";
                return response;
            }

            // Prevent duplicate active borrow
            bool alreadyBorrowed = await _context.BorrowTransactions.AnyAsync(x =>
                x.UserId == reservation.UserId &&
                x.BookId == reservation.BookId &&
                x.Status == "Borrowed");

            if (alreadyBorrowed)
            {
                response.Success = false;
                response.Message = "Student already borrowed this book.";
                return response;
            }

            // Maximum of 3 active borrowed books
            bool hasReachedLimit = await _context.BorrowTransactions
                .CountAsync(x =>
                    x.UserId == reservation.UserId &&
                    x.Status == "Borrowed") >= 3;

            if (hasReachedLimit)
            {
                response.Success = false;
                response.Message = "Student has reached the maximum borrowing limit.";
                return response;
            }

            var borrow = new BorrowTransaction
            {
                UserId = reservation.UserId,
                BookId = reservation.BookId,
                BorrowDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(7),
                ReturnDate = null,
                RenewalCount = 0,
                Status = "Borrowed",
                Fine = 0,
                Remarks = reservation.Remarks,
                CreatedAt = DateTime.Now
            };

            _context.BorrowTransactions.Add(borrow);

            reservation.Book.AvailableCopies--;

            reservation.Status = "Completed";

            await _context.SaveChangesAsync();

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