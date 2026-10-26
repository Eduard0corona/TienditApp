using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The CFDI 4.0 invoice of an order (Fase 28A, ADR 0045): a named receiver, the folio and the idempotency key saved before
/// the PAC is asked, the stamp, the refusal and the cancellation, which keep the invoice instead of deleting it.
/// </summary>
public class OrderInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string Uuid = "5f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string Document = "{\"conceptos\":[]}";
    private const string CertificateNumber = "30001000000400002434";

    [Fact]
    public void Receiver_ShouldBeKeptInCapitals_AsTheSatReadsIt()
    {
        var receiver = CfdiReceiver.Create(" ure180429tm6 ", " Universidad Robotica Española ", " 86991 ", "601", "g03");

        Assert.Equal(("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            (receiver.Rfc, receiver.Name, receiver.PostalCode, receiver.FiscalRegime, receiver.CfdiUse));
        Assert.Equal(receiver, CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"));
    }

    [Theory]
    [InlineData("URE18042", "UNIVERSIDAD", "86991", "601", "G03", "RFC")]
    [InlineData("XAXX010101000", "PUBLICO EN GENERAL", "86991", "601", "S01", "factura global")]
    [InlineData("URE180429TM6", " ", "86991", "601", "G03", "nombre o razón social")]
    [InlineData("URE180429TM6", "UNIVERSIDAD", "8699", "601", "G03", "código postal")]
    [InlineData("URE180429TM6", "UNIVERSIDAD", "86991", "999", "G03", "régimen")]
    [InlineData("URE180429TM6", "UNIVERSIDAD", "86991", "616", "G03", "régimen")]
    [InlineData("URE180429TM6", "UNIVERSIDAD", "86991", "601", "X99", "uso del CFDI")]
    public void Receiver_ShouldBeRefused_WithoutValidFiscalData(
        string rfc, string name, string postalCode, string regime, string use, string message)
    {
        var error = Assert.Throws<DomainException>(() => CfdiReceiver.Create(rfc, name, postalCode, regime, use));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    // Purchases and expenses for those with an activity, not for wages.
    [InlineData("601", "G03", true)]
    [InlineData("626", "G01", true)]
    [InlineData("605", "G03", false)]
    [InlineData("616", "G03", false)]
    // Personal deductions for individuals, not for companies.
    [InlineData("612", "D01", true)]
    [InlineData("605", "D01", true)]
    [InlineData("601", "D01", false)]
    [InlineData("626", "D01", false)]
    // Without fiscal effects, anyone; payroll, only wages.
    [InlineData("616", "S01", true)]
    [InlineData("601", "S01", true)]
    [InlineData("605", "CN01", true)]
    [InlineData("601", "CN01", false)]
    // Outside the catalogs, nothing.
    [InlineData("999", "S01", false)]
    [InlineData("601", "X99", false)]
    public void Use_ShouldApplyOnlyToTheRegimesTheSatAllows(string regime, string use, bool applies)
    {
        Assert.Equal(applies, SatCatalog.UseAppliesToRegime(use, regime));
    }

    [Fact]
    public void Receiver_ShouldBeRefused_WithAUseItsRegimeCannotAskFor()
    {
        // The PAC would refuse it (CFDI40161) and the folio would be spent.
        var error = Assert.Throws<DomainException>(() => CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD", "86991", "601", "d01"));
        Assert.Equal(CfdiReceiver.UseNotForRegimeMessage("D01", "601"), error.Message);
    }

    [Fact]
    public void Receiver_ShouldTakeUpToTheSatsLengthOfName()
    {
        CfdiReceiver.Create("URE180429TM6", new string('A', CfdiReceiver.MaxNameLength), "86991", "601", "G03");
        Assert.Throws<DomainException>(() =>
            CfdiReceiver.Create("URE180429TM6", new string('A', CfdiReceiver.MaxNameLength + 1), "86991", "601", "G03"));
    }

    [Fact]
    public void Customer_ShouldKeepItsFiscalData_ButNotTheGeneralPublic()
    {
        var customer = new Customer(TenantId, "Universidad", "5512345678", null);
        customer.SetFiscalData(Receiver(), null);
        Assert.Equal(Receiver(), customer.FiscalData);
        customer.SetFiscalData(null, null);
        Assert.Null(customer.FiscalData);

        var publicGeneral = new Customer(TenantId, "Público en general", null, null, isPublicGeneral: true);
        var error = Assert.Throws<DomainException>(() => publicGeneral.SetFiscalData(Receiver(), null));
        Assert.Equal(Customer.PublicGeneralFiscalDataMessage, error.Message);
        publicGeneral.SetFiscalData(null, null);
    }

    [Fact]
    public void Request_ShouldFixTheFolioDateAndKey_BeforeThePacIsAsked()
    {
        var invoice = Invoice();

        Assert.Equal((TenantId, OrderId, "F", 7L, "F-7", Now, OrderInvoiceStatus.Stamping),
            (invoice.TenantId, invoice.OrderId, invoice.Serie, invoice.Folio, invoice.Number, invoice.IssuedAt, invoice.Status));
        Assert.NotEqual(Guid.Empty, invoice.IdempotencyKey);
        Assert.Equal(("04", "PUE", 100m, 16m, 116m), (invoice.PaymentForm, invoice.PaymentMethod, invoice.SubTotal, invoice.TaxTotal, invoice.Total));
        Assert.Equal((CashierId, Now, Document), (invoice.RequestedByUserId, invoice.RequestedAt, invoice.Document));
        Assert.True(invoice.IsLive);
        Assert.Null(invoice.Uuid);
        // Each invoice has its own key: asking for another one never repeats the last.
        Assert.NotEqual(invoice.IdempotencyKey, Invoice().IdempotencyKey);
    }

    [Fact]
    public void Request_ShouldNormalizeTheSerie_AndRefuseOneThatIsNotLettersAndNumbers()
    {
        Assert.Equal("FA", Invoice(serie: " fa ").Serie);
        Assert.Throws<DomainException>(() => Invoice(serie: "F-1"));
        Assert.Throws<DomainException>(() => Invoice(serie: ""));
        Assert.Throws<DomainException>(() => Invoice(serie: new string('F', OrderInvoice.MaxSerieLength + 1)));
        Assert.Throws<DomainException>(() => Invoice(folio: 0));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("")]
    [InlineData("001")]
    public void Request_ShouldTakeTheFormThePaidSaleWasPaidWith(string form)
    {
        var error = Assert.Throws<DomainException>(() => Invoice(paymentForm: form));
        Assert.Contains("forma en que se pagó", error.Message);
    }

    [Fact]
    public void Request_InInstallments_ShouldTakeOnlyTheFormToBeDefined()
    {
        // A sale not paid in full (Fase 28C): PPD with «Por definir», and each payment gets its complement.
        var invoice = Invoice(paymentForm: "99", paymentMethod: "PPD");

        Assert.Equal(("99", "PPD", true), (invoice.PaymentForm, invoice.PaymentMethod, invoice.IsPaidInInstallments));
        Assert.False(Invoice().IsPaidInInstallments);
        var error = Assert.Throws<DomainException>(() => Invoice(paymentForm: "04", paymentMethod: "PPD"));
        Assert.Contains("Por definir", error.Message);
        Assert.Throws<DomainException>(() => Invoice(paymentForm: "99", paymentMethod: "PUD"));
        Assert.Throws<DomainException>(() => Invoice(paymentForm: "04", paymentMethod: ""));
    }

    [Theory]
    [InlineData(100, 16, 117)]
    [InlineData(100, 16, 0)]
    [InlineData(-1, 1, 0)]
    [InlineData(100.001, 16, 116.001)]
    public void Request_ShouldRefuseAmountsThatDoNotAddUp(decimal subTotal, decimal tax, decimal total)
    {
        Assert.Throws<DomainException>(() => Invoice(subTotal: subTotal, tax: tax, total: total));
    }

    [Fact]
    public void Request_ShouldNeedItsBusinessOrderAndWhoAsks()
    {
        Assert.Throws<DomainException>(() => OrderInvoice.Request(
            TenantId, Guid.Empty, "F", 1, Receiver(), "04", Document, 100m, 16m, 116m, CashierId, Now));
        Assert.Throws<DomainException>(() => OrderInvoice.Request(
            TenantId, OrderId, "F", 1, Receiver(), "04", Document, 100m, 16m, 116m, Guid.Empty, Now));
        Assert.Throws<DomainException>(() => OrderInvoice.Request(
            TenantId, OrderId, "F", 1, Receiver(), "04", " ", 100m, 16m, 116m, CashierId, Now));
    }

    [Fact]
    public void Stamp_ShouldKeepTheUuidAndXml_AndHearingItAgainChangesNothing()
    {
        var invoice = Invoice();

        invoice.Stamp(Uuid, "pac-123", Now.AddSeconds(3), "orders/t/invoices/202610/f-7.xml");
        invoice.AttachPdf("orders/t/invoices/202610/f-7.pdf");

        Assert.Equal((OrderInvoiceStatus.Stamped, Uuid.ToUpperInvariant(), "pac-123", Now.AddSeconds(3)),
            (invoice.Status, invoice.Uuid, invoice.ProviderInvoiceId, invoice.StampedAt));
        Assert.Equal(("orders/t/invoices/202610/f-7.xml", "orders/t/invoices/202610/f-7.pdf"), (invoice.XmlFileKey, invoice.PdfFileKey));
        invoice.Stamp(Uuid.ToUpperInvariant(), "pac-123", Now.AddMinutes(1), "other.xml");
        Assert.Equal((Now.AddSeconds(3), "orders/t/invoices/202610/f-7.xml"), (invoice.StampedAt, invoice.XmlFileKey));
        Assert.Throws<DomainException>(() => invoice.Stamp(Guid.NewGuid().ToString(), "pac-124", Now, "x.xml"));
        Assert.True(invoice.IsLive);
    }

    [Theory]
    [InlineData("not-a-uuid", "pac-1", "f.xml")]
    [InlineData(Uuid, " ", "f.xml")]
    [InlineData(Uuid, "pac-1", " ")]
    public void Stamp_ShouldNeedTheUuidThePacIdAndTheXml(string uuid, string providerId, string xmlKey)
    {
        Assert.Throws<DomainException>(() => Invoice().Stamp(uuid, providerId, Now, xmlKey));
    }

    [Fact]
    public void Pdf_ShouldOnlyGoWithAStampedInvoice()
    {
        Assert.Throws<DomainException>(() => Invoice().AttachPdf("f.pdf"));
    }

    [Fact]
    public void Refuse_ShouldKeepThePacsReason_AndFreeTheOrder()
    {
        var invoice = Invoice();

        invoice.Refuse("  El nombre del receptor no coincide con el registrado en el SAT.  ", Now.AddSeconds(2));

        Assert.Equal((OrderInvoiceStatus.Refused, "El nombre del receptor no coincide con el registrado en el SAT.", Now.AddSeconds(2)),
            (invoice.Status, invoice.FailureMessage, invoice.RefusedAt));
        Assert.False(invoice.IsLive);
        Assert.Throws<DomainException>(() => invoice.Stamp(Uuid, "pac-1", Now, "f.xml"));
        Assert.Equal(OrderInvoice.MaxFailureMessageLength,
            RefusedWith(new string('x', OrderInvoice.MaxFailureMessageLength + 10)).FailureMessage!.Length);
        Assert.Equal("El PAC no timbró la factura.", RefusedWith(" ").FailureMessage);
    }

    [Fact]
    public void Cancellation_ShouldWaitForTheReceiver_ThenKeepTheInvoiceCancelled()
    {
        var invoice = Stamped();

        invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now.AddHours(1));
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now.AddHours(1)),
            (invoice.Status, invoice.CancellationMotive, invoice.CancellationRequestedByUserId, invoice.CancellationRequestedAt));
        Assert.True(invoice.IsLive);
        // Asking again for the same motive (a lost answer) is not an error.
        invoice.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now.AddHours(2));
        Assert.Equal(Now.AddHours(1), invoice.CancellationRequestedAt);

        invoice.Cancel(Now.AddDays(1));
        invoice.Cancel(Now.AddDays(2));
        Assert.Equal((OrderInvoiceStatus.Cancelled, Now.AddDays(1)), (invoice.Status, invoice.CancelledAt));
        Assert.False(invoice.IsLive);
        Assert.Equal(Uuid.ToUpperInvariant(), invoice.Uuid);
        var error = Assert.Throws<DomainException>(() =>
            invoice.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, OwnerId, Now.AddDays(3)));
        Assert.Contains("ya está cancelada", error.Message);
    }

    [Fact]
    public void Cancellation_RefusedByTheReceiver_ShouldLeaveItStamped_WithTheReason()
    {
        var invoice = Stamped();
        invoice.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, OwnerId, Now.AddHours(1));

        invoice.KeepAfterRefusedCancellation("El receptor rechazó la cancelación.");

        Assert.Equal((OrderInvoiceStatus.Stamped, "El receptor rechazó la cancelación."), (invoice.Status, invoice.FailureMessage));
        // It can be asked again: the reason goes away.
        invoice.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, OwnerId, Now.AddDays(4));
        Assert.Null(invoice.FailureMessage);
    }

    [Theory]
    [InlineData(CfdiCancellationMotive.ErrorsWithReplacement)]
    [InlineData(CfdiCancellationMotive.NominativeInGlobalInvoice)]
    public void Cancellation_ShouldOfferOnlyTheMotivesWithoutAnotherInvoice_UntilOneReplacesIt(CfdiCancellationMotive motive)
    {
        var error = Assert.Throws<DomainException>(() => Stamped().RequestCancellation(motive, OwnerId, Now));
        Assert.Contains("02", error.Message);
        Assert.False(Stamped().TakesCancellationMotive(motive));
    }

    [Fact]
    public void Replacement_ShouldLeaveTheInvoiceStanding_ButNoLongerTheOrdersOne_UntilItIsCancelledWithMotive01()
    {
        // Fase 28G: F-8 replaces F-7, whose receiver had the wrong postal code; F-7 is cancelled with 01 once F-8 stands.
        var original = Stamped();
        var replacement = Invoice(folio: 8);
        Assert.True(original.IsCurrent);

        original.ReplaceWith(replacement);

        Assert.Equal(replacement.Id, original.ReplacedByInvoiceId);
        Assert.Equal((OrderInvoiceStatus.Stamped, true, false), (original.Status, original.IsLive, original.IsCurrent));
        Assert.True(replacement.IsCurrent);
        Assert.True(original.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithReplacement));
        Assert.False(original.TakesCancellationMotive(CfdiCancellationMotive.NominativeInGlobalInvoice));

        original.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, OwnerId, Now.AddMinutes(5));
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.ErrorsWithReplacement), (original.Status, original.CancellationMotive));
        original.Cancel(Now.AddMinutes(6));

        // It stays cancelled and replaced: the relation is part of its history.
        Assert.Equal((OrderInvoiceStatus.Cancelled, replacement.Id), (original.Status, original.ReplacedByInvoiceId));
        Assert.False(original.IsCurrent);
    }

    [Fact]
    public void Replacement_ShouldBeOfTheSameOrder_JustRequested_AndOfAStampedInvoiceNothingElseReplaces()
    {
        var original = Stamped();
        Assert.Throws<DomainException>(() => original.ReplaceWith(original));
        Assert.Throws<DomainException>(() => original.ReplaceWith(OrderInvoice.Request(
            TenantId, Guid.NewGuid(), "F", 8, Receiver(), "04", Document, 100m, 16m, 116m, CashierId, Now)));
        Assert.Throws<DomainException>(() => original.ReplaceWith(OrderInvoice.Request(
            Guid.NewGuid(), OrderId, "F", 8, Receiver(), "04", Document, 100m, 16m, 116m, CashierId, Now)));
        Assert.Throws<DomainException>(() => original.ReplaceWith(Stamped()));
        Assert.Throws<ArgumentNullException>(() => original.ReplaceWith(null!));
        Assert.Null(original.ReplacedByInvoiceId);

        original.ReplaceWith(Invoice(folio: 8));
        Assert.Equal(OrderInvoice.AlreadyReplacedMessage,
            Assert.Throws<DomainException>(() => original.ReplaceWith(Invoice(folio: 9))).Message);

        // Only a stamped one: not one still stamping, refused, waiting for its cancellation or cancelled.
        Assert.Throws<DomainException>(() => Invoice().ReplaceWith(Invoice(folio: 8)));
        Assert.Throws<DomainException>(() => RefusedWith("CFDI40145").ReplaceWith(Invoice(folio: 8)));
        var cancelling = Stamped();
        cancelling.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now);
        Assert.Contains("espera su cancelación", Assert.Throws<DomainException>(() => cancelling.ReplaceWith(Invoice(folio: 8))).Message);
        cancelling.Cancel(Now);
        Assert.Throws<DomainException>(() => cancelling.ReplaceWith(Invoice(folio: 8)));
    }

    [Fact]
    public void RefusedReplacement_ShouldMakeTheInvoiceTheOrdersOneAgain()
    {
        var original = Stamped();
        var replacement = Invoice(folio: 8);
        original.ReplaceWith(replacement);

        // Another invoice's refusal changes nothing.
        original.KeepAfterRefusedReplacement(Guid.NewGuid());
        Assert.Equal(replacement.Id, original.ReplacedByInvoiceId);

        original.KeepAfterRefusedReplacement(replacement.Id);
        Assert.Null(original.ReplacedByInvoiceId);
        Assert.True(original.IsCurrent);
        Assert.False(original.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithReplacement));
        // Hearing it again changes nothing, and another one can replace it.
        original.KeepAfterRefusedReplacement(replacement.Id);
        var second = Invoice(folio: 9);
        original.ReplaceWith(second);
        Assert.Equal(second.Id, original.ReplacedByInvoiceId);

        // Once its cancellation was asked for, its replacement stood: it stays replaced.
        original.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, OwnerId, Now);
        Assert.Throws<DomainException>(() => original.KeepAfterRefusedReplacement(second.Id));
    }

    [Fact]
    public void FailedReplacementCancellation_ShouldKeepTheReason_UntilItIsAskedAgain()
    {
        var original = Stamped();
        Assert.Throws<DomainException>(() => original.KeepAfterFailedReplacementCancellation("no"));
        original.ReplaceWith(Invoice(folio: 8));

        original.KeepAfterFailedReplacementCancellation("  El SAT todavía no recibe la factura que la sustituye.  ");
        Assert.Equal((OrderInvoiceStatus.Stamped, "El SAT todavía no recibe la factura que la sustituye."),
            (original.Status, original.FailureMessage));
        original.KeepAfterFailedReplacementCancellation(new string('x', 600));
        Assert.Equal(OrderInvoice.MaxFailureMessageLength, original.FailureMessage!.Length);
        original.KeepAfterFailedReplacementCancellation(" ");
        Assert.Equal("El SAT no canceló la factura sustituida.", original.FailureMessage);

        original.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, OwnerId, Now);
        Assert.Null(original.FailureMessage);
        Assert.Throws<DomainException>(() => original.KeepAfterFailedReplacementCancellation("no"));
    }

    [Fact]
    public void ReplacedInvoice_ShouldStillBeCancelledWithoutRelation_AsTheSatSaysWhen01Fails()
    {
        var original = Stamped();
        original.ReplaceWith(Invoice(folio: 8));

        original.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, OwnerId, Now);
        original.Cancel(Now);

        Assert.Equal((OrderInvoiceStatus.Cancelled, CfdiCancellationMotive.ErrorsWithoutReplacement),
            (original.Status, original.CancellationMotive));
    }

    [Fact]
    public void Cancellation_ShouldOnlyGoForAStampedInvoice()
    {
        Assert.Throws<DomainException>(() => Invoice().RequestCancellation(CfdiCancellationMotive.NotCarriedOut, OwnerId, Now));
        Assert.Throws<DomainException>(() => Stamped().RequestCancellation(CfdiCancellationMotive.NotCarriedOut, Guid.Empty, Now));
        Assert.Throws<DomainException>(() => Stamped().Cancel(Now));
        Assert.Throws<DomainException>(() => Stamped().KeepAfterRefusedCancellation("no"));
    }

    [Fact]
    public void Profile_ShouldStartWithSerieF_AndKeepOnlyWhatIdentifiesTheCsd()
    {
        var profile = InvoicingProfile.Create(TenantId);
        Assert.Equal(("F", false), (profile.Serie, profile.HasCertificate));
        Assert.False(profile.CanSealFor("EKU9003173C9", Now));

        profile.UseCertificate(CertificateNumber, " eku9003173c9 ", Now.AddYears(-1), Now.AddYears(3), OwnerId, Now);

        Assert.Equal((CertificateNumber, "EKU9003173C9", Now.AddYears(-1), Now.AddYears(3), Now, OwnerId),
            (profile.CertificateNumber, profile.CertificateRfc, profile.CertificateValidFrom, profile.CertificateValidTo,
                profile.CertificateUploadedAt, profile.CertificateUploadedByUserId));
        Assert.True(profile.CanSealFor("eku9003173c9", Now));
        // Another business's RFC, before it starts or once it expired: it does not seal.
        Assert.False(profile.CanSealFor("URE180429TM6", Now));
        Assert.False(profile.CanSealFor("EKU9003173C9", Now.AddYears(-2)));
        Assert.False(profile.CanSealFor("EKU9003173C9", Now.AddYears(3)));
    }

    [Fact]
    public void Profile_ShouldWarnThirtyDaysBeforeTheCsdExpires()
    {
        var profile = InvoicingProfile.Create(TenantId);
        profile.UseCertificate(CertificateNumber, "EKU9003173C9", Now.AddYears(-1), Now.AddDays(40), OwnerId, Now);

        Assert.False(profile.CertificateExpiresSoon(Now));
        Assert.True(profile.CertificateExpiresSoon(Now.AddDays(10)));
        Assert.False(profile.CertificateExpiresSoon(Now.AddDays(41)));
    }

    [Theory]
    [InlineData("3000100000040000226", "EKU9003173C9", 1, 3)]
    [InlineData("300010000004000022AB", "EKU9003173C9", 1, 3)]
    [InlineData(CertificateNumber, "EKU900317", 1, 3)]
    [InlineData(CertificateNumber, "EKU9003173C9", 3, 1)]
    [InlineData(CertificateNumber, "EKU9003173C9", -3, -1)]
    public void Profile_ShouldRefuseACsdThatIsNotValid(string number, string rfc, int fromYears, int toYears)
    {
        var profile = InvoicingProfile.Create(TenantId);
        Assert.Throws<DomainException>(() =>
            profile.UseCertificate(number, rfc, Now.AddYears(fromYears), Now.AddYears(toYears), OwnerId, Now));
        Assert.False(profile.HasCertificate);
    }

    [Fact]
    public void Profile_ShouldForgetItsCsd_AndChangeItsSerie()
    {
        var profile = InvoicingProfile.Create(TenantId);
        profile.UseCertificate(CertificateNumber, "EKU9003173C9", Now.AddYears(-1), Now.AddYears(3), OwnerId, Now);

        profile.RemoveCertificate();
        profile.ChangeSerie(" ta ");

        Assert.Equal(("TA", false, null, null), (profile.Serie, profile.HasCertificate, profile.CertificateRfc, profile.CertificateValidTo));
        Assert.Throws<DomainException>(() => profile.ChangeSerie("T A"));

        // The payment complements' serie (Fase 28C): «P» until the business chooses another one, even the invoices' own.
        Assert.Equal("P", profile.PaymentSerie);
        profile.ChangePaymentSerie(" ta ");
        Assert.Equal("TA", profile.PaymentSerie);
        // Its refusal names the serie of the complements, not the invoices'.
        Assert.Equal("La serie de los complementos de pago tiene de 1 a 10 letras o números.",
            Assert.Throws<DomainException>(() => profile.ChangePaymentSerie("P-1")).Message);
        Assert.Throws<DomainException>(() => profile.ChangePaymentSerie(" "));
        Assert.Equal("TA", profile.PaymentSerie);
        Assert.Throws<DomainException>(() => InvoicingProfile.Create(Guid.Empty));
    }

    private static CfdiReceiver Receiver() => CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03");

    private static OrderInvoice Invoice(
        string serie = "F", long folio = 7, string paymentForm = "04", decimal subTotal = 100m, decimal tax = 16m, decimal total = 116m,
        string paymentMethod = "PUE")
        => OrderInvoice.Request(
            TenantId, OrderId, serie, folio, Receiver(), paymentForm, Document, subTotal, tax, total, CashierId, Now, paymentMethod);

    private static OrderInvoice Stamped()
    {
        var invoice = Invoice();
        invoice.Stamp(Uuid, "pac-123", Now.AddSeconds(3), "f-7.xml");
        return invoice;
    }

    private static OrderInvoice RefusedWith(string message)
    {
        var invoice = Invoice();
        invoice.Refuse(message, Now);
        return invoice;
    }
}
