namespace LibraryLending.Domain;

/// <summary>A bibliographic edition. Its physical items are represented separately by BookCopy.</summary>
public sealed class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Isbn { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public int Year { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public ICollection<BookAuthor> BookAuthors { get; set; } = new List<BookAuthor>();
    public ICollection<BookCopy> Copies { get; set; } = new List<BookCopy>();
    public Category? Category { get; set; }
}
