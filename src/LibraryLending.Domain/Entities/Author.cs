namespace LibraryLending.Domain;

public sealed class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<BookAuthor> BookAuthors { get; set; } = new List<BookAuthor>();
}

public sealed class BookAuthor
{
    public int BookId { get; set; }
    public int AuthorId { get; set; }
    public int DisplayOrder { get; set; }
    public Book Book { get; set; } = null!;
    public Author Author { get; set; } = null!;
}
