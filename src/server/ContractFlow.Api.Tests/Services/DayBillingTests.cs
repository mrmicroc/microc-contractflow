using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>
/// The shared per-day pipeline (design §5): one minimum and one OT threshold
/// per contract-day, split across work packages; pay parts at their own
/// agreement; shares always sum exactly.
/// </summary>
public class DayBillingTests
{
    private static readonly Guid Contract = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Project = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid PkgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PkgB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly DateOnly Monday = new(2029, 1, 8);

    private static DayRow Row(decimal hours, Guid? pkg = null, Guid? contract = null, bool booked = false) =>
        new(Guid.NewGuid(), Project, contract ?? Contract, pkg, EntryBilling.Contract, booked, hours);

    private static ContractTerms Terms(decimal? min = null, bool pad = false) => new()
    {
        ContractId = Contract, Currency = "SEK", HolidayCalendar = "SE",
        HourlyRate = 1000m, MinBillableHoursPerDay = min, PadEnabled = pad,
    };

    [Fact]
    public void BillDay_TwoPackages_ShareOneMinimum()
    {
        var parts = DayBilling.BillDay(Monday, [Row(2m, PkgA), Row(2m, PkgB)],
            _ => Terms(min: 8m), _ => (null, null));
        Assert.Equal(2, parts.Count);
        Assert.Equal(8m, parts.Sum(p => p.Line!.EffectiveHours));
        Assert.Equal(8000m, parts.Sum(p => p.Line!.Amount));
    }

    [Fact]
    public void BillDay_OtherContractHours_NeverTouchThisMinimum()
    {
        var parts = DayBilling.BillDay(Monday, [Row(3m), Row(3m, contract: Other)],
            c => c == Contract ? Terms(min: 8m) : Terms(), _ => (null, null));
        Assert.Equal(8m, parts.Single(p => p.ContractId == Contract).Line!.EffectiveHours);
        Assert.Equal(3m, parts.Single(p => p.ContractId == Other).Line!.EffectiveHours);
    }

    [Fact]
    public void BillDay_PadCoveredBookedRows_AreNotPriced()
    {
        var parts = DayBilling.BillDay(Monday, [Row(8m, booked: true)],
            _ => Terms(min: 10m, pad: true), _ => (null, null));
        var part = Assert.Single(parts);
        Assert.Equal(BillPartKind.PadCovered, part.Kind);
        Assert.Equal(8m, part.Entries.Single().Hours);
        Assert.Equal(0m, part.Entries.Single().MinimumAdjustment);
    }

    [Fact]
    public void PayDay_EachPackageAtItsOwnAgreement()
    {
        var pkgAgreement = new CompensationAgreement { Currency = "SEK", HourlyRate = 700m, WorkPackageId = PkgA };
        var defaultAgreement = new CompensationAgreement { Currency = "SEK", HourlyRate = 600m };
        var parts = DayBilling.PayDay(Monday, [Row(4m, PkgA), Row(4m, PkgB)],
            (_, wp) => wp == PkgA ? pkgAgreement : defaultAgreement, _ => "SE");
        Assert.Equal(5200m, parts.Sum(p => p.Line.Amount));
    }

    [Fact]
    public void Distribute_RemainderLandsOnLastEntry()
    {
        var rows = new[] { Row(1m), Row(1m), Row(1m) };
        var shares = DayBilling.Distribute(rows, 8m);
        Assert.Equal(8m, shares.Sum(s => s.Hours));
        Assert.Equal(2, shares.Count(s => s.Hours == 2.67m));
        Assert.All(shares, s => Assert.Equal(s.Hours - 1m, s.MinimumAdjustment));
    }
}
