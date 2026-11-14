using FluentAssertions;
using LibraryLending.Application;
using NSubstitute;
using Xunit;

namespace LibraryLending.Tests.Unit;

public sealed class LibraryCalendarTests
{
    [Theory]
    [InlineData("2026-11-15T02:00:00Z", 2026, 11, 14)]
    [InlineData("2026-11-15T05:00:00Z", 2026, 11, 15)]
    [InlineData("2026-07-15T03:59:59Z", 2026, 7, 14)]
    [InlineData("2026-07-15T04:00:00Z", 2026, 7, 15)]
    public void Fine_calendar_uses_configured_library_timezone_in_winter_and_summer(string instant, int year, int month, int day)
    {
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));
        var options = new LibraryOptions { TimeZoneId = "America/New_York" };

        options.Today(clock).Should().Be(new DateOnly(year, month, day));
    }
}
