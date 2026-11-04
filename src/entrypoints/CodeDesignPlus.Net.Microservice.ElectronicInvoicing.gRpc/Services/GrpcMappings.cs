using System.Globalization;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.IssueElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Queries.GetIssuer;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;
using NodaTime.Serialization.Protobuf;
using NodaTime.Text;
using Domain = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;
using Enums = CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Services;

/// <summary>
/// Field by field between the gRPC messages and the application: a mapping by convention would publish by itself
/// whatever a later member is called.
/// </summary>
public static class GrpcMappings
{
    /// <summary>
    /// The content of a document as the application receives it.
    /// </summary>
    /// <param name="content">The message.</param>
    /// <returns>The content.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>304</c>, <c>312</c> or <c>313</c> when an id, a date or the content is not well written.</exception>
    public static DocumentContentInput ToInput(DocumentContent content)
    {
        InfrastructureGuard.IsNull(content, Infrastructure.Errors.InvalidContent);

        return new DocumentContentInput(
            BuyerOf(content.Buyer),
            [.. content.Lines.Select(LineOf)],
            new DocumentTotalsInput(content.Totals?.Currency ?? string.Empty, content.Totals?.GrossAmount ?? 0, content.Totals?.DiscountAmount ?? 0, content.Totals?.TaxAmount ?? 0, content.Totals?.WithholdingAmount ?? 0, content.Totals?.PrepaymentAmount ?? 0, content.Totals?.PayableAmount ?? 0),
            [.. content.Prepayments.Select(x => new Domain.ValueObjects.Prepayment(x.Id, x.Amount, DateOf(x.ReceivedOn)))],
            new Domain.ValueObjects.PaymentMeans(content.PaymentMeans?.FormCode ?? string.Empty, content.PaymentMeans?.MethodCode ?? string.Empty),
            DateOf(content.DueDate),
            [.. content.Notes],
            content.BillingReference is null ? null : new BillingReferenceInput(
                content.BillingReference.HasDocumentId ? GuidOf(content.BillingReference.DocumentId) : null,
                content.BillingReference.HasPrefix ? content.BillingReference.Prefix : null,
                content.BillingReference.HasConsecutive ? content.BillingReference.Consecutive : null,
                content.BillingReference.HasCufe ? content.BillingReference.Cufe : null,
                content.BillingReference.HasIssueDate ? DateOf(content.BillingReference.IssueDate) : null),
            content.HasCorrectionConceptCode ? content.CorrectionConceptCode : null);
    }

    /// <summary>
    /// The type of a document as the domain names it.
    /// </summary>
    /// <param name="type">The type of the message.</param>
    /// <returns>The type.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>307</c> when it is not set.</exception>
    public static Enums.ElectronicDocumentType TypeOf(DocumentType type)
    {
        InfrastructureGuard.IsTrue(type == DocumentType.Unspecified || !Enum.IsDefined((Enums.ElectronicDocumentType)(int)type), Infrastructure.Errors.InvalidDocumentType);

        return (Enums.ElectronicDocumentType)(int)type;
    }

    /// <summary>
    /// An id of the message.
    /// </summary>
    /// <param name="value">The id as text.</param>
    /// <returns>The id.</returns>
    /// <exception cref="CodeDesignPlusException">Thrown with <c>304</c> when it is not a valid identifier.</exception>
    public static Guid GuidOf(string value)
    {
        InfrastructureGuard.IsFalse(Guid.TryParse(value, out var id), Infrastructure.Errors.InvalidId);

        return id;
    }

    /// <summary>
    /// An optional id of the message.
    /// </summary>
    /// <param name="has">Whether the message carries it.</param>
    /// <param name="value">The id as text.</param>
    /// <returns>The id, or null when it is not there or empty.</returns>
    public static Guid? OptionalGuidOf(bool has, string value) => has && !string.IsNullOrWhiteSpace(value) ? GuidOf(value) : null;

    /// <summary>
    /// The answer to an issue or a retry.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The message.</returns>
    public static IssueDocumentResponse ToResponse(IssuedDocumentDto document)
    {
        var response = new IssueDocumentResponse { Id = document.Id.ToString(), Status = (DocumentStatus)(int)document.Status };

        if (document.Prefix is not null)
        {
            response.Prefix = document.Prefix;
        }

        if (document.Consecutive is { } consecutive)
        {
            response.Consecutive = consecutive;
        }

        return response;
    }

    /// <summary>
    /// A document as the message carries it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The message.</returns>
    public static GetDocumentResponse ToResponse(ElectronicDocumentDto document)
    {
        var response = new GetDocumentResponse
        {
            Id = document.Id.ToString(),
            Tenant = document.Tenant.ToString(),
            Reference = document.Reference,
            IssuerId = document.IssuerId.ToString(),
            Type = (DocumentType)(int)document.Type,
            Status = (DocumentStatus)(int)document.Status,
            Contingency = document.Contingency,
            Attempts = document.Attempts,
            Stalled = document.StalledAt is not null,
            Xml = FileOf(document.Xml),
            Pdf = FileOf(document.Pdf),
            AttachedDocument = FileOf(document.AttachedDocument)
        };

        if (document.Prefix is not null)
        {
            response.Prefix = document.Prefix;
        }

        if (document.Consecutive is { } consecutive)
        {
            response.Consecutive = consecutive;
        }

        if (document.Cufe is not null)
        {
            response.Cufe = document.Cufe;
        }

        if (document.IssueDate is { } issueDate)
        {
            response.IssueDate = LocalDatePattern.Iso.Format(issueDate);
        }

        if (document.ValidatedAt is { } validatedAt)
        {
            response.ValidatedAt = validatedAt.ToTimestamp();
        }

        if (document.Rejection is { } rejection)
        {
            response.Rejection = new Rejection { Origin = (RejectionOrigin)(int)rejection.Origin, At = rejection.At.ToTimestamp() };
            response.Rejection.Reasons.AddRange(rejection.Reasons);
        }

        return response;
    }

    /// <summary>
    /// An issuer as the message carries it.
    /// </summary>
    /// <param name="issuer">The issuer, or null when the tenant has none.</param>
    /// <returns>The message.</returns>
    public static GetIssuerResponse ToResponse(IssuerDto? issuer)
    {
        if (issuer is null)
        {
            return new GetIssuerResponse { Registered = false, Quota = new Quota() };
        }

        var response = new GetIssuerResponse
        {
            Registered = true,
            Id = issuer.Id.ToString(),
            Status = (IssuerStatus)(int)issuer.Status,
            Environment = (IssuerEnvironment)(int)issuer.Environment,
            CanIssue = issuer.CanIssue,
            Provider = issuer.Provider,
            Quota = new Quota { Limit = issuer.Quota.Limit, Used = issuer.Quota.Used, Remaining = issuer.Quota.Remaining }
        };

        if (issuer.Quota.PeriodStart is { } start)
        {
            response.Quota.PeriodStart = start.ToTimestamp();
        }

        if (issuer.Quota.PeriodEnd is { } end)
        {
            response.Quota.PeriodEnd = end.ToTimestamp();
        }

        if (issuer.Capabilities is { } capabilities)
        {
            response.Capabilities = new Capabilities
            {
                SupportsPrepayments = capabilities.SupportsPrepayments,
                ExternalBillingReference = capabilities.ExternalBillingReference,
                HabilitationByApi = capabilities.HabilitationByApi
            };

            response.Capabilities.DocumentTypes.AddRange(capabilities.DocumentTypes.Select(x => (DocumentType)(int)x));
        }

        if (issuer.SuspensionReasonCode is not null)
        {
            response.SuspensionReasonCode = issuer.SuspensionReasonCode;
        }

        return response;
    }

    private static Domain.ValueObjects.Buyer BuyerOf(Buyer? buyer)
    {
        InfrastructureGuard.IsNull(buyer, Infrastructure.Errors.InvalidContent);

        return new Domain.ValueObjects.Buyer(
            buyer.PersonTypeCode,
            buyer.IdentificationTypeCode,
            buyer.IdentificationNumber,
            buyer.HasCheckDigit ? buyer.CheckDigit : null,
            buyer.Name,
            buyer.HasFirstName ? buyer.FirstName : null,
            buyer.HasLastName ? buyer.LastName : null,
            buyer.HasEmail ? buyer.Email : null,
            buyer.HasPhone ? buyer.Phone : null,
            buyer.HasTaxLiabilityCode ? buyer.TaxLiabilityCode : null,
            [.. buyer.TaxResponsibilities],
            buyer.Address is null ? null : new PostalAddress(buyer.Address.Street, buyer.Address.CountryCode, buyer.Address.CountryName, buyer.Address.RegionCode, buyer.Address.RegionName, buyer.Address.CityCode, buyer.Address.CityName, buyer.Address.HasPostalCode ? buyer.Address.PostalCode : null));
    }

    private static DocumentLine LineOf(Line line)
    {
        InfrastructureGuard.IsFalse(decimal.TryParse(line.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity), Infrastructure.Errors.InvalidContent);

        return new DocumentLine(line.Code, line.Description, quantity, line.UnitCode, line.UnitPrice, line.DiscountAmount, line.Amount, [.. line.Taxes.Select(TaxOf)], [.. line.Withholdings.Select(TaxOf)]);
    }

    private static Domain.ValueObjects.LineTax TaxOf(LineTax tax) => new(tax.Code, tax.Name, tax.RateBasisPoints, tax.BaseAmount, tax.Amount);

    private static LocalDate DateOf(string value)
    {
        var parsed = LocalDatePattern.Iso.Parse(value ?? string.Empty);

        InfrastructureGuard.IsFalse(parsed.Success, Infrastructure.Errors.InvalidDate);

        return parsed.Value;
    }

    private static StoredFile? FileOf(CodeDesignPlus.Net.ValueObjects.FileServer.File? file) => file is null ? null : new StoredFile
    {
        Id = file.Id.ToString(),
        Target = file.Target,
        Name = file.Name,
        Extension = file.Extension,
        MimeType = file.MimeType,
        Size = file.Size
    };
}
