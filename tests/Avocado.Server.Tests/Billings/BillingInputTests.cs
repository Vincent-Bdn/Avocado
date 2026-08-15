using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Avocado.Server.Features.Billings.Enums;

namespace Avocado.Server.Tests.Billings;

public class BillingInvoiceInputTests
{
    private static BillingInvoiceInput Valid => new(new DateOnly(2026, 8, 15), 240_000, "F-2026-014");

    [Fact]
    public void AcceptsAWellFormedInvoice() => Assert.Null(Valid.Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-240_000)]
    public void RefusesAnAmountThatIsNotPositive(long cents) =>
        Assert.NotNull((Valid with { AmountExclVatCents = cents }).Validate());

    /// <summary>
    /// A settlement date on an unpaid invoice is a contradiction, and one that would quietly skew
    /// every trésorerie figure that reads PaidOn.
    /// </summary>
    [Fact]
    public void RefusesASettlementDateOnAnUnpaidInvoice() =>
        Assert.NotNull((Valid with { IsPaid = false, PaidOn = new DateOnly(2026, 9, 1) }).Validate());

    [Fact]
    public void AcceptsASettlementDateOnAPaidInvoice() =>
        Assert.Null((Valid with { IsPaid = true, PaidOn = new DateOnly(2026, 9, 1) }).Validate());

    /// <summary>Paid without a date is allowed: the date is a refinement, not a precondition.</summary>
    [Fact]
    public void AcceptsAPaidInvoiceWithNoDate() =>
        Assert.Null((Valid with { IsPaid = true, PaidOn = null }).Validate());
}

public class BillingLedgerInputTests
{
    private static BillingLedgerInput Receipt =>
        new(BillingMovementKind.Receipt, new DateOnly(2026, 8, 15), 150_000, "Provision");

    [Fact]
    public void AcceptsAWellFormedMovement() => Assert.Null(Receipt.Validate());

    /// <summary>
    /// The sign comes from the kind and never from the number. A client that could send a negative
    /// amount could turn a débours into a receipt through a typo, and the two move the trésorerie in
    /// opposite directions.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-150_000)]
    public void RefusesAnAmountThatCarriesItsOwnSign(long cents) =>
        Assert.NotNull((Receipt with { AmountCents = cents }).Validate());

    [Fact]
    public void RefusesAMovementWithNoLabel() =>
        Assert.NotNull((Receipt with { Label = "   " }).Validate());

    [Fact]
    public void AReceiptIsPositiveAndADeboursIsNegative()
    {
        Assert.Equal(150_000, Receipt.SignedAmountCents);
        Assert.Equal(-150_000, (Receipt with { Kind = BillingMovementKind.Disbursement }).SignedAmountCents);
    }

    /// <summary>The magnitude is the same either way: only the direction differs.</summary>
    [Fact]
    public void TheKindChangesOnlyTheDirection()
    {
        var debours = Receipt with { Kind = BillingMovementKind.Disbursement };

        Assert.Equal(Receipt.AmountCents, Math.Abs(debours.SignedAmountCents));
    }
}

public class BillingCostInputTests
{
    private static BillingCostInput Valid =>
        new(new DateOnly(2026, 8, 15), "Rétrocession d'honoraires", "Me Martin", 120_000, null, null, null);

    [Fact]
    public void AcceptsAWellFormedCost() => Assert.Null(Valid.Validate());

    /// <summary>Direction is the type, never the sign, exactly as for a ledger movement.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-120_000)]
    public void RefusesAnAmountThatIsNotPositive(long cents) =>
        Assert.NotNull((Valid with { AmountExclVatCents = cents }).Validate());

    [Fact]
    public void RefusesACostWithNoLabel() =>
        Assert.NotNull((Valid with { Label = "  " }).Validate());

    [Fact]
    public void RefusesASettlementDateOnAnUnsettledCost() =>
        Assert.NotNull((Valid with { IsPaid = false, PaidOn = new DateOnly(2026, 9, 1) }).Validate());

    /// <summary>
    /// The kind is free text and optional on purpose: a practice that meets a cost nobody thought of
    /// should not have to wait for a release to record it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Traduction assermentée")]
    public void AcceptsAnyKindOrNoneAtAll(string? kind) =>
        Assert.Null((Valid with { Kind = kind }).Validate());
}
