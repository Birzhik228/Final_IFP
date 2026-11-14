using System.Security.Claims;
using LibraryLending.Application;
using LibraryLending.Domain;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace LibraryLending.Infrastructure.Seeding;

public static class DemoSeeder
{
    public const string Password = "LibraryDemo1!";
    public static async Task SeedAsync(LibraryDbContext db, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles, TimeProvider clock, LibraryOptions options)
    {
        foreach (var role in new[] { "Librarian", "Member" }) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        async Task<IdentityUser> Account(string email, string role)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null) { user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true }; var result = await users.CreateAsync(user, Password); if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description))); }
            if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role); return user;
        }
        await Account("librarian@library.local", "Librarian");
        if (await db.Books.AnyAsync()) return;
        var names = new[] { "Alex Morgan", "Jamie Chen", "Sam Rivera", "Taylor Brooks", "Jordan Patel", "Casey Kim", "Robin Ellis", "Morgan Reed", "Avery Park", "Riley James" };
        var members = new List<Member>();
        for (var i = 0; i < 10; i++) { var email = $"member{i + 1}@library.local"; var user = await Account(email, "Member"); members.Add(new Member { Name = names[i], Email = email, IdentityUserId = user.Id, Phone = $"+1 555 010 {i + 1:00}", Status = MembershipStatus.Active, BorrowingLimit = 5 }); }
        db.Members.AddRange(members);
        var data = new (string Title, string Author, string Category, int Year)[]{
   ("Clean Code","Robert C. Martin","Technology",2008),("Pride and Prejudice","Jane Austen","Fiction",1813),("The Pragmatic Programmer","Andrew Hunt; David Thomas","Technology",1999),("Design Patterns","Erich Gamma; Richard Helm; Ralph Johnson; John Vlissides","Technology",1994),("Refactoring","Martin Fowler","Technology",1999),
   ("The Hobbit","J. R. R. Tolkien","Fiction",1937),("Jane Eyre","Charlotte Brontë","Fiction",1847),("Little Women","Louisa May Alcott","Fiction",1868),("Frankenstein","Mary Shelley","Fiction",1818),("The Time Machine","H. G. Wells","Science Fiction",1895),
   ("The War of the Worlds","H. G. Wells","Science Fiction",1898),("Dune","Frank Herbert","Science Fiction",1965),("Foundation","Isaac Asimov","Science Fiction",1951),("The Left Hand of Darkness","Ursula K. Le Guin","Science Fiction",1969),("A Wizard of Earthsea","Ursula K. Le Guin","Fantasy",1968),
   ("The Little Prince","Antoine de Saint-Exupéry","Fiction",1943),("Alice's Adventures in Wonderland","Lewis Carroll","Fantasy",1865),("Treasure Island","Robert Louis Stevenson","Adventure",1883),("The Call of the Wild","Jack London","Adventure",1903),("The Secret Garden","Frances Hodgson Burnett","Fiction",1911),
   ("A Brief History of Time","Stephen Hawking","Science",1988),("Cosmos","Carl Sagan","Science",1980),("The Selfish Gene","Richard Dawkins","Science",1976),("Silent Spring","Rachel Carson","Science",1962),("The Elements of Style","William Strunk Jr.; E. B. White","Writing",1959),
   ("On Writing","Stephen King","Writing",2000),("The Art of War","Sun Tzu","History",1900),("Meditations","Marcus Aurelius","Philosophy",180),("Walden","Henry David Thoreau","Philosophy",1854),("The Republic","Plato","Philosophy",1900)};
        var categories = data.Select(x => x.Category).Distinct().ToDictionary(x => x, x => new Category { Name = x });
        var authors = new Dictionary<string, Author>(); var books = new List<Book>();
        for (var i = 0; i < data.Length; i++)
        {
            var item = data[i]; var prefix = $"97800000{i + 1:0000}"; var checksum = (10 - prefix.Select((c, n) => (c - '0') * (n % 2 == 0 ? 1 : 3)).Sum() % 10) % 10;
            var b = new Book { Title = item.Title, Isbn = prefix + checksum, Category = categories[item.Category], Year = item.Year, Description = $"Explore {item.Title} by {item.Author.Replace(';', ',')}. This catalogue entry is part of the library's demonstration collection.", CoverUrl = "" };
            var n = 0; foreach (var name in item.Author.Split(';', StringSplitOptions.TrimEntries)) { if (!authors.TryGetValue(name, out var a)) { a = new Author { Name = name }; authors.Add(name, a); } b.BookAuthors.Add(new BookAuthor { Book = b, Author = a, DisplayOrder = n++ }); }
            for (var c = 1; c <= 2; c++) b.Copies.Add(new BookCopy { Book = b, Barcode = $"LIB-{i + 1:000}-{c:00}", Status = CopyStatus.Available }); books.Add(b);
        }
        db.Books.AddRange(books); await db.SaveChangesAsync();
        var now = clock.GetUtcNow().UtcDateTime; var today = options.Today(clock);
        void Loan(Book b, int copyIndex, Member m, int daysAgo, bool overdue = false, bool returned = false)
        {
            var copy = b.Copies.ElementAt(copyIndex); if (!returned) copy.Status = CopyStatus.Borrowed;
            db.Loans.Add(new Loan { BookCopyId = copy.Id, MemberId = m.Id, BorrowedAtUtc = now.AddDays(-daysAgo), DueDate = overdue ? today.AddDays(-daysAgo + 14) : today.AddDays(returned ? -1 : 7), DailyFineMinor = options.DailyFineMinor, ReturnedAtUtc = returned ? now.AddDays(-2) : null, FinalFineMinor = returned ? 0 : null });
        }
        Loan(books[0], 0, members[0], 21, true); Loan(books[0], 1, members[2], 18, true);
        Loan(books[5], 0, members[3], 8); Loan(books[6], 0, members[4], 20, true); Loan(books[20], 0, members[5], 12);
        for (var i = 0; i < 12; i++) Loan(books[(i % 4) + 1], 0, members[(i % 8) + 1], 30 + i, false, true);
        db.Reservations.AddRange(new Reservation { BookId = books[0].Id, MemberId = members[1].Id, CreatedAtUtc = now.AddDays(-2), Status = ReservationStatus.Waiting }, new Reservation { BookId = books[0].Id, MemberId = members[6].Id, CreatedAtUtc = now.AddDays(-1), Status = ReservationStatus.Waiting });
        await db.SaveChangesAsync();
    }
}
