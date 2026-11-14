namespace LibraryLending.Domain;

public sealed class BookCopy
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public CopyStatus Status { get; set; } = CopyStatus.Available;
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public Book Book { get; set; } = null!;
}
