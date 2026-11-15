using LibraryLending.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace LibraryLending.Infrastructure.Persistence;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCopy> Copies => Set<BookCopy>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<BorrowRequest> BorrowRequests => Set<BorrowRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Book>().HasIndex(x => x.Isbn).IsUnique();
        b.Entity<Book>().HasIndex(x => new { x.CategoryId, x.Title });
        b.Entity<Book>().Property(x => x.Title).HasMaxLength(200);
        b.Entity<Book>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Author>().HasIndex(x => x.Name).IsUnique(); b.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        b.Entity<BookAuthor>().HasKey(x => new { x.BookId, x.AuthorId });
        b.Entity<BookAuthor>().HasOne(x => x.Book).WithMany(x => x.BookAuthors).HasForeignKey(x => x.BookId);
        b.Entity<BookAuthor>().HasOne(x => x.Author).WithMany(x => x.BookAuthors).HasForeignKey(x => x.AuthorId);
        b.Entity<BookCopy>().HasIndex(x => x.Barcode).IsUnique(); b.Entity<BookCopy>().HasIndex(x => new { x.BookId, x.Status });
        b.Entity<BookCopy>().HasOne(x => x.Book).WithMany(x => x.Copies).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BookCopy>().Property(x => x.Status).HasConversion<string>(); b.Entity<BookCopy>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Member>().Property(x => x.Status).HasConversion<string>(); b.Entity<Member>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Member>().HasIndex(x => x.Email).IsUnique(); b.Entity<Member>().HasIndex(x => x.IdentityUserId).IsUnique();
        b.Entity<Member>().HasOne<IdentityUser>().WithOne().HasForeignKey<Member>(x => x.IdentityUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Member>().ToTable(t => t.HasCheckConstraint("CK_Member_Limit", "BorrowingLimit BETWEEN 1 AND 50"));
        b.Entity<Loan>().HasOne(x => x.BookCopy).WithMany().HasForeignKey(x => x.BookCopyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasIndex(x => x.BookCopyId).IsUnique().HasFilter("\"ReturnedAtUtc\" IS NULL");
        b.Entity<Loan>().HasIndex(x => new { x.MemberId, x.ReturnedAtUtc }); b.Entity<Loan>().HasIndex(x => new { x.ReturnedAtUtc, x.DueDate });
        b.Entity<Loan>().ToTable(t => t.HasCheckConstraint("CK_Loan_Fine", "DailyFineMinor >= 0 AND (FinalFineMinor IS NULL OR FinalFineMinor >= 0)"));
        b.Entity<Reservation>().Property(x => x.Status).HasConversion<string>(); b.Entity<Reservation>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Reservation>().HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Reservation>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Reservation>().HasOne(x => x.AssignedCopy).WithMany().HasForeignKey(x => x.AssignedCopyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Reservation>().HasOne<Loan>().WithMany().HasForeignKey(x => x.FulfilledLoanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Reservation>().HasIndex(x => new { x.BookId, x.MemberId }).IsUnique().HasFilter("\"Status\" IN ('Waiting','Ready')");
        b.Entity<Reservation>().HasIndex(x => x.AssignedCopyId).IsUnique().HasFilter("\"Status\" = 'Ready'");
        b.Entity<Reservation>().HasIndex(x => new { x.BookId, x.Status, x.CreatedAtUtc, x.Id }); b.Entity<Reservation>().HasIndex(x => new { x.Status, x.PickupDeadlineUtc });
        b.Entity<Notification>().HasIndex(x => x.DedupeKey).IsUnique(); b.Entity<Notification>().HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        b.Entity<Notification>().HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BorrowRequest>().Property(x => x.Status).HasConversion<string>();
        b.Entity<BorrowRequest>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<BorrowRequest>().Property(x => x.ReviewNote).HasMaxLength(500);
        b.Entity<BorrowRequest>().HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BorrowRequest>().HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BorrowRequest>().HasOne(x => x.Loan).WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BorrowRequest>().HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BorrowRequest>().HasIndex(x => new { x.BookId, x.MemberId }).IsUnique().HasFilter("\"Status\" = 'Pending'");
        b.Entity<BorrowRequest>().HasIndex(x => x.LoanId).IsUnique().HasFilter("\"LoanId\" IS NOT NULL");
        b.Entity<BorrowRequest>().HasIndex(x => new { x.Status, x.CreatedAtUtc, x.Id });
        b.Entity<BorrowRequest>().HasIndex(x => new { x.MemberId, x.CreatedAtUtc });
        b.Entity<BorrowRequest>().ToTable(t => t.HasCheckConstraint("CK_BorrowRequest_Review",
            "length(\"ReviewNote\") <= 500 AND ((\"Status\" = 'Pending' AND \"ReviewedAtUtc\" IS NULL AND \"ReviewerUserId\" IS NULL AND \"LoanId\" IS NULL) OR (\"Status\" = 'Accepted' AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewerUserId\" IS NOT NULL AND \"LoanId\" IS NOT NULL) OR (\"Status\" = 'Rejected' AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewerUserId\" IS NOT NULL AND \"LoanId\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"ReviewedAtUtc\" IS NOT NULL AND \"LoanId\" IS NULL))"));
    }
}
