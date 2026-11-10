using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using NodaTime;
using IssuerEnvironment = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums.IssuerEnvironment;
using IssuerStatus = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums.IssuerStatus;
using Buyer = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.Buyer;
using DocumentContent = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.DocumentContent;
using BillingReference = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.BillingReference;
using PaymentMeans = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.PaymentMeans;
using LineTax = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.LineTax;
using Prepayment = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects.Prepayment;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;

/// <summary>
/// Invented data shared by the tests: no tax id, email or phone belongs to anyone.
/// </summary>
public static class TestData
{
    public static readonly Guid Tenant = Guid.Parse("aa000000-0000-4000-8000-000000000001");

    public static readonly Guid User = Guid.Parse("aa000000-0000-4000-8000-000000000002");

    public static readonly Guid SystemUser = Guid.Parse("aa000000-0000-4000-8000-000000000003");

    public static readonly Guid PlatformTenant = PlatformScope.Tenant;

    public static readonly Instant Now = Instant.FromUtc(2026, 11, 4, 15, 0);

    public static PostalAddress Address() => new("Calle 1 # 2-3", "CO", "Colombia", "11", "Bogota", "11001", "Bogota", "110111");

    public static Buyer Buyer(string identification = "900000001") => new("1", "31", identification, "7", "Comprador de Prueba SAS", null, null, "comprador@example.com", "3000000000", "ZZ", ["R-99-PN"], Address());

    /// <summary>
    /// Two units of 10.000,00 with 19 % VAT: 20.000,00 gross and 3.800,00 of tax, in minor units of a currency with two decimals.
    /// </summary>
    public static DocumentLine Line() => new("SVC-1", "Servicio de prueba", 2, "94", 1_000_000, 0, 2_000_000, [new LineTax("01", "IVA", 1900, 2_000_000, 380_000)], []);

    public static DocumentTotals Totals() => new("COP", 2, 2_000_000, 0, 380_000, 0, 0, 2_380_000);

    public static DocumentContent InvoiceContent(string identification = "900000001") => new(Buyer(identification), [Line()], Totals(), [], new PaymentMeans("1", "10"), null, [], null, null);

    public static BillingReference ExternalReference() => new(null, "SETP", 990000001, "cufe-of-the-corrected-invoice", new LocalDate(2026, 11, 1));

    public static DocumentContent NoteContent() => InvoiceContent() with { BillingReference = ExternalReference(), CorrectionConceptCode = "2" };

    public static IssuerFiscalData FiscalData() => new("CO", "31", "900000002-1", "Emisor de Prueba SAS", "1", "48", ["O-13"], Address(), "emisor@example.com", "3000000001", "America/Bogota");

    public static IssuerMandate Mandate() => new("1.0", Now, User, null);

    public static IssuerAggregate RegisteredIssuer(string provider = "Simulated", Guid? tenant = null) => IssuerAggregate.Register(tenant ?? Tenant, FiscalData(), provider, User);

    public static IssuerAggregate EnabledIssuer(string provider = "Simulated", Guid? tenant = null)
    {
        var issuer = RegisteredIssuer(provider, tenant);

        issuer.AcceptMandate(Mandate(), User);
        issuer.Enable(IssuerEnvironment.Test, User);
        issuer.GetAndClearEvents();

        return issuer;
    }

    public static NumberingResolution Resolution(ElectronicDocumentType type = ElectronicDocumentType.Invoice, string prefix = "SETP", long from = 990000000, long to = 995000000) => new(type, "18760000001", "technical-key-of-test", prefix, from, to, new LocalDate(2026, 1, 1), new LocalDate(2027, 12, 31));

    public static NumberingRangeAggregate Range(IssuerAggregate issuer, ElectronicDocumentType type = ElectronicDocumentType.Invoice, long from = 990000000, long to = 995000000) => NumberingRangeAggregate.Create(issuer.Tenant, issuer.Id, Resolution(type, from: from, to: to), NumberingSource.Platform, User);

    public static ElectronicDocumentAggregate Document(Guid? id = null, ElectronicDocumentType type = ElectronicDocumentType.Invoice, DocumentContent? content = null, Guid? precededBy = null, Guid? tenant = null)
    {
        var owner = tenant ?? Tenant;

        var document = ElectronicDocumentAggregate.Create(id ?? Guid.NewGuid(), owner, IssuerAggregate.IdFor(owner), type, "orders:order:1", content ?? (DocumentContent.IsNote(type) ? NoteContent() : InvoiceContent()), precededBy, Now, User);

        document.GetAndClearEvents();

        return document;
    }
}
