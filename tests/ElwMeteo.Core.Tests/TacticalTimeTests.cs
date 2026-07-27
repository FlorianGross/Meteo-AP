using ElwMeteo.Core.Time;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class TacticalTimeTests
{
    [Fact]
    public void FormatZulu_ProducesCanonicalDtg()
    {
        var instant = new DateTimeOffset(2026, 7, 27, 10, 55, 0, TimeSpan.Zero);
        Assert.Equal("271055ZJUL26", TacticalTime.FormatZulu(instant));
    }

    [Fact]
    public void Format_UsesBravoForCentralEuropeanSummerTime()
    {
        // 12:55 in UTC+02:00 is the same instant as 10:55 Zulu.
        var instant = new DateTimeOffset(2026, 7, 27, 12, 55, 0, TimeSpan.FromHours(2));
        Assert.Equal("271255BJUL26", TacticalTime.Format(instant));
    }

    [Fact]
    public void Format_UsesAlphaForCentralEuropeanWinterTime()
    {
        var instant = new DateTimeOffset(2026, 1, 3, 6, 5, 0, TimeSpan.FromHours(1));
        Assert.Equal("030605AJAN26", TacticalTime.Format(instant));
    }

    [Theory]
    [InlineData(0, 'Z')]
    [InlineData(1, 'A')]
    [InlineData(2, 'B')]
    [InlineData(9, 'I')]
    // "J" is reserved for local time, so UTC+10 is "K", not "J".
    [InlineData(10, 'K')]
    [InlineData(12, 'M')]
    [InlineData(-1, 'N')]
    [InlineData(-5, 'R')]
    [InlineData(-12, 'Y')]
    public void ZoneLetter_SkipsJuliett(int offsetHours, char expected)
    {
        Assert.Equal(expected, TacticalTime.ZoneLetter(TimeSpan.FromHours(offsetHours)));
    }

    [Fact]
    public void ZoneLetter_FallsBackToLocalForHalfHourOffsets()
    {
        // India is UTC+05:30 and has no military zone letter.
        Assert.Equal('J', TacticalTime.ZoneLetter(TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public void Format_PadsSingleDigitDayAndTime()
    {
        var instant = new DateTimeOffset(2026, 3, 5, 4, 7, 0, TimeSpan.Zero);
        Assert.Equal("050407ZMAR26", TacticalTime.FormatZulu(instant));
    }

    [Theory]
    [InlineData(1, 1, "JAN")]
    [InlineData(5, 1, "MAY")]
    [InlineData(12, 1, "DEC")]
    public void Format_UsesEnglishMonthAbbreviations(int month, int day, string expected)
    {
        var instant = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero);
        Assert.Contains(expected, TacticalTime.FormatZulu(instant));
    }


}
