namespace LibraryLending.Domain;

public sealed class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
}
