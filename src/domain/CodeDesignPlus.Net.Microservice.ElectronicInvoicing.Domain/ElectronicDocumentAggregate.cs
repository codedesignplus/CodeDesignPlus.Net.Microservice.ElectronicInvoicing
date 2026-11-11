namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain;

/// <summary>
/// An electronic document that an issuer sends to the tax authority through a technology provider.
/// </summary>
/// <remarks>
/// Its id is the one the caller derives from its own source, so repeating the request does not issue twice; the
/// <see cref="ContentHash"/> tells a retry of the same request from a different document reusing the id.
/// <para>
/// The life is <see cref="ElectronicDocumentStatus.Draft"/> → <see cref="ElectronicDocumentStatus.Sent"/> →
/// <see cref="ElectronicDocumentStatus.Validated"/> or <see cref="ElectronicDocumentStatus.Rejected"/>, and a rejected
/// document is corrected on the same id and the same number (a rejected document did not use its number before the
/// tax authority). Validated is final: an error is corrected with a note.
/// </para>
/// <para>
/// The consumer that sends it, the status job and a retry from the caller write the same document, hence
/// <see cref="IVersionedAggregate"/>.
/// </para>
/// </remarks>
public class ElectronicDocumentAggregate(Guid id) : AggregateRootBase(id), IVersionedAggregate
{
    /// <inheritdoc/>
    public long Version { get; private set; }

    /// <summary>
    /// Tenant that issues the document.
    /// </summary>
    public Guid Tenant { get; private set; }

    /// <summary>
    /// The id of the issuer.
    /// </summary>
    public Guid IssuerId { get; private set; }

    /// <summary>
    /// The type of the document.
    /// </summary>
    public ElectronicDocumentType Type { get; private set; }

    /// <summary>
    /// Opaque text of the caller that comes back in the events, so each caller can tell which documents are its own.
    /// </summary>
    public string Reference { get; private set; } = null!;

    /// <summary>
    /// What the document says.
    /// </summary>
    public DocumentContent Content { get; private set; } = null!;

    /// <summary>
    /// SHA-256 of the type, the reference, the document that precedes it and the content.
    /// </summary>
    public string ContentHash { get; private set; } = null!;

    /// <summary>
    /// The document that must be validated before this one is sent.
    /// </summary>
    public Guid? PrecededBy { get; private set; }

    /// <summary>
    /// The number of the document, once it has one.
    /// </summary>
    public DocumentNumber? Number { get; private set; }

    /// <summary>
    /// The range the number was taken from, when this service numbered it.
    /// </summary>
    public Guid? NumberingRangeId { get; private set; }

    /// <summary>
    /// The status of the document.
    /// </summary>
    public ElectronicDocumentStatus Status { get; private set; }

    /// <summary>
    /// The id of the document in the provider.
    /// </summary>
    public string? ProviderDocumentId { get; private set; }

    /// <summary>
    /// The last status the provider gave, as it came.
    /// </summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>
    /// Whether the tax authority is not answering and the provider keeps the document in contingency.
    /// </summary>
    public bool Contingency { get; private set; }

    /// <summary>
    /// The CUFE (CUDE in notes) given by the tax authority.
    /// </summary>
    public string? Cufe { get; private set; }

    /// <summary>
    /// The issue date the provider put on the document.
    /// </summary>
    public LocalDate? IssueDate { get; private set; }

    /// <summary>
    /// When the tax authority validated it.
    /// </summary>
    public Instant? ValidatedAt { get; private set; }

    /// <summary>
    /// Why it was rejected, while it is.
    /// </summary>
    public DocumentRejection? Rejection { get; private set; }

    /// <summary>
    /// The files of the validated document in ms-filestorage.
    /// </summary>
    public DocumentFiles? Files { get; private set; }

    /// <summary>
    /// How many sends or checks it took since it was created or corrected.
    /// </summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// When the status job looks at it again; null when there is nothing to wait for.
    /// </summary>
    public Instant? NextCheckAt { get; private set; }

    /// <summary>
    /// When the provider received it for the first time; the use of the provider's plan counts from here.
    /// </summary>
    public Instant? FirstSentAt { get; private set; }

    /// <summary>
    /// When it was looked at for the last time.
    /// </summary>
    public Instant? LastCheckAt { get; private set; }

    /// <summary>
    /// What happened the last time it was looked at: a short code of this service, for the record of each round.
    /// </summary>
    public string? LastCheckOutcome { get; private set; }

    /// <summary>
    /// When it ran out of retries without a final answer, while it waits for someone to retry it.
    /// </summary>
    public Instant? StalledAt { get; private set; }

    /// <summary>
    /// Until when a round of sending or checking holds it; nobody else talks to the provider about it meanwhile.
    /// </summary>
    public Instant? LeasedUntil { get; private set; }

    /// <summary>
    /// How many times its content was corrected after a rejection: each correction is a new send to the provider, also
    /// when the corrected content is the same, so the providers that dedupe the sends by a key tell them apart.
    /// </summary>
    /// <remarks>
    /// A member and not an argument of the constructor: the documents saved before it existed read it as zero.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>
    /// How many times the files of the validated document were asked for and not kept: the tax authority validated it,
    /// but its XML is not in ms-filestorage yet.
    /// </summary>
    /// <remarks>
    /// A member and not an argument of the constructor: the documents saved before it existed read it as zero.
    /// </remarks>
    public int FilesAttempts { get; private set; }

    /// <summary>
    /// When the files of the validated document ran out of retries without being kept, while it waits for someone to
    /// retry it.
    /// </summary>
    /// <remarks>
    /// A member and not an argument of the constructor: the documents saved before it existed read it as null.
    /// </remarks>
    public Instant? FilesStalledAt { get; private set; }

    /// <summary>
    /// Whether the document is validated and its XML is not kept: the issuer must keep it, so it is asked for again.
    /// </summary>
    public bool MissingFiles => Status == ElectronicDocumentStatus.Validated && Files?.Xml is null;

    /// <summary>
    /// Whether the files of the validated document are still being asked for: missing and not out of retries.
    /// </summary>
    public bool FilesPending => MissingFiles && FilesStalledAt is null;

    /// <summary>
    /// The hash that identifies a request: the same id with another hash is another document reusing the id.
    /// </summary>
    /// <param name="type">The type of the document.</param>
    /// <param name="reference">The caller's opaque reference.</param>
    /// <param name="precededBy">The document that must be validated first.</param>
    /// <param name="content">What the document says.</param>
    /// <returns>The hash.</returns>
    public static string ContentHashOf(ElectronicDocumentType type, string reference, Guid? precededBy, DocumentContent content) => Canonicalizer.Hash(Canonicalizer.Join(type, reference, precededBy, content.Canonical()));

    /// <summary>
    /// Creates a document waiting to be sent.
    /// </summary>
    /// <param name="id">The id the caller derived from its own source.</param>
    /// <param name="tenant">The tenant that issues the document.</param>
    /// <param name="issuerId">The id of the issuer.</param>
    /// <param name="type">The type of the document.</param>
    /// <param name="reference">The caller's opaque reference.</param>
    /// <param name="content">What the document says.</param>
    /// <param name="precededBy">The document that must be validated before this one is sent.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="createdBy">Who asked for the document.</param>
    /// <returns>The document in <see cref="ElectronicDocumentStatus.Draft"/>, with its queued event.</returns>
    public static ElectronicDocumentAggregate Create(Guid id, Guid tenant, Guid issuerId, ElectronicDocumentType type, string reference, DocumentContent content, Guid? precededBy, Instant now, Guid createdBy)
    {
        DomainGuard.GuidIsEmpty(id, Errors.ElectronicDocumentIdIsRequired);
        DomainGuard.GuidIsEmpty(tenant, Errors.TenantIsRequired);
        DomainGuard.GuidIsEmpty(issuerId, Errors.IssuerIsRequired);
        DomainGuard.IsFalse(Enum.IsDefined(type), Errors.DocumentTypeNotSupported);
        DomainGuard.IsNullOrWhiteSpace(reference, Errors.ReferenceIsRequired);
        DomainGuard.IsTrue(reference.Length > FieldLength.Title, Errors.ReferenceIsTooLong);
        DomainGuard.IsNull(content, Errors.ContentIsRequired);
        DomainGuard.IsTrue(precededBy == id, Errors.DocumentCannotPrecedeItself);
        DomainGuard.GuidIsEmpty(createdBy, Errors.CreatedByIsRequired);

        content.EnsureValid(type);

        var document = new ElectronicDocumentAggregate(id)
        {
            Tenant = tenant,
            IssuerId = issuerId,
            Type = type,
            Reference = reference,
            Content = content,
            ContentHash = ContentHashOf(type, reference, precededBy, content),
            PrecededBy = precededBy is null || precededBy == Guid.Empty ? null : precededBy,
            Status = ElectronicDocumentStatus.Draft,
            NextCheckAt = now,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now
        };

        document.AddEvent(ElectronicDocumentQueuedDomainEvent.Create(id, tenant, type, reference, document.PrecededBy));

        return document;
    }

    /// <summary>
    /// Gives the document the number reserved for it.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="numberingRangeId">The range it was taken from.</param>
    /// <returns>False when it already had a number, so nothing is written.</returns>
    public bool AssignNumber(DocumentNumber number, Guid? numberingRangeId)
    {
        DomainGuard.IsNull(number, Errors.NumberingRangeIsInvalid);

        if (Number is not null)
        {
            return false;
        }

        DomainGuard.IsTrue(Status != ElectronicDocumentStatus.Draft, Errors.DocumentStatusDoesNotAllowIt);

        Number = number;
        NumberingRangeId = numberingRangeId;

        return true;
    }

    /// <summary>
    /// Takes the document for one round of sending or checking: whoever saves the claim first is the only one that talks
    /// to the provider about it, and the others find the version moved and leave it.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="leaseUntil">Until when the round holds it; if the process dies, the job takes it again after this.</param>
    /// <returns>False when the document no longer waits for anything, or another round holds it or it waits its turn, so nothing is written.</returns>
    /// <remarks>
    /// A validated document whose files are pending is due also without a next look: the ones validated before the files
    /// were retried kept none.
    /// </remarks>
    public bool Claim(Instant now, Instant leaseUntil)
    {
        var due = FilesPending ? NextCheckAt is null || NextCheckAt <= now : !IsFinal && NextCheckAt <= now;

        if (!due || StalledAt is not null || LeasedUntil > now)
        {
            return false;
        }

        LeasedUntil = leaseUntil;
        NextCheckAt = leaseUntil;
        LastCheckAt = now;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Records one round of the sending or the checking that did not end the document, and when to look again.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="outcome">What happened, as a short code.</param>
    /// <param name="nextCheckAt">When to look again.</param>
    public void RecordAttempt(Instant now, string outcome, Instant nextCheckAt)
    {
        EnsureNotFinal();

        Attempts++;
        LastCheckAt = now;
        LastCheckOutcome = outcome;
        NextCheckAt = nextCheckAt;
        LeasedUntil = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// The provider received the document; the answer of the tax authority is pending.
    /// </summary>
    /// <param name="providerDocumentId">The id in the provider.</param>
    /// <param name="providerStatus">The status the provider gave, as it came.</param>
    /// <param name="number">The number the provider gave, when it numbers.</param>
    /// <param name="contingency">Whether the tax authority is not answering.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="nextCheckAt">When to ask for the answer.</param>
    public void MarkSent(string providerDocumentId, string? providerStatus, DocumentNumber? number, bool contingency, Instant now, Instant nextCheckAt)
    {
        DomainGuard.IsNullOrWhiteSpace(providerDocumentId, Errors.ProviderDocumentIdIsRequired);
        EnsureNotFinal();

        ProviderDocumentId = providerDocumentId;
        ProviderStatus = providerStatus;
        Number ??= number;
        Contingency = contingency;
        Status = ElectronicDocumentStatus.Sent;
        FirstSentAt ??= now;

        RecordAttempt(now, contingency ? "contingency" : "sent", nextCheckAt);
    }

    /// <summary>
    /// The provider still has no final answer for a sent document.
    /// </summary>
    /// <param name="providerStatus">The status the provider gave, as it came.</param>
    /// <param name="contingency">Whether the tax authority is not answering.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="nextCheckAt">When to ask again.</param>
    public void KeepWaiting(string? providerStatus, bool contingency, Instant now, Instant nextCheckAt)
    {
        DomainGuard.IsTrue(Status != ElectronicDocumentStatus.Sent, Errors.DocumentStatusDoesNotAllowIt);

        ProviderStatus = providerStatus;
        Contingency = contingency;

        RecordAttempt(now, contingency ? "contingency" : "processing", nextCheckAt);
    }

    /// <summary>
    /// The tax authority validated the document.
    /// </summary>
    /// <param name="providerDocumentId">The id in the provider.</param>
    /// <param name="cufe">The CUFE (CUDE in notes).</param>
    /// <param name="number">The number the provider gave, when it numbers.</param>
    /// <param name="issueDate">The issue date the provider put.</param>
    /// <param name="providerStatus">The status the provider gave, as it came.</param>
    /// <param name="files">The files stored in ms-filestorage.</param>
    /// <param name="now">The current instant.</param>
    public void Validate(string providerDocumentId, string cufe, DocumentNumber? number, LocalDate? issueDate, string? providerStatus, DocumentFiles? files, Instant now)
    {
        DomainGuard.IsNullOrWhiteSpace(providerDocumentId, Errors.ProviderDocumentIdIsRequired);
        DomainGuard.IsNullOrWhiteSpace(cufe, Errors.CufeIsRequired);
        EnsureNotFinal();

        Number ??= number;

        DomainGuard.IsNull(Number, Errors.NumberIsRequired);

        ProviderDocumentId = providerDocumentId;
        ProviderStatus = providerStatus;
        Cufe = cufe;
        IssueDate = issueDate;
        Files = files;
        Contingency = false;
        Status = ElectronicDocumentStatus.Validated;
        ValidatedAt = now;
        FirstSentAt ??= now;
        LastCheckAt = now;
        LastCheckOutcome = "validated";
        Attempts++;
        NextCheckAt = null;
        LeasedUntil = null;
        StalledAt = null;
        UpdatedAt = now;

        // The adapters write the due date of the XML as DueDateOn(the issue date); the legal issue date is the one the
        // provider answered with.
        var dueDate = issueDate is { } issued ? Content.DueDateOn(issued) : Content.DueDate;

        AddEvent(new ElectronicDocumentValidatedDomainEvent(Id, Tenant, IssuerId, Type, Reference, Number!.Prefix, Number.Consecutive, cufe, issueDate, now, files?.Xml, files?.Pdf, Content.Totals) { AttachedDocument = files?.AttachedDocument, DueDate = dueDate });
    }

    /// <summary>
    /// The files of the validated document are kept in ms-filestorage, and whoever waited for them is told.
    /// </summary>
    /// <param name="files">The files stored, with the XML.</param>
    /// <param name="now">The current instant.</param>
    public void KeepFiles(DocumentFiles files, Instant now)
    {
        DomainGuard.IsFalse(Status == ElectronicDocumentStatus.Validated, Errors.DocumentStatusDoesNotAllowIt);
        DomainGuard.IsNull(files?.Xml, Errors.FilesAreRequired);

        Files = files;
        FilesStalledAt = null;
        NextCheckAt = null;
        LeasedUntil = null;
        LastCheckAt = now;
        LastCheckOutcome = "files-stored";
        UpdatedAt = now;

        AddEvent(new ElectronicDocumentFilesKeptDomainEvent(Id, Tenant, IssuerId, Type, Reference, Number?.Prefix, Number?.Consecutive, files.Xml!, files.AttachedDocument, files.Pdf));
    }

    /// <summary>
    /// The files of the validated document could not be kept this time. They are asked for again later and, after the
    /// configured attempts, the document is announced instead of retrying for ever.
    /// </summary>
    /// <param name="partial">What could be kept, if anything; the XML is missing.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="nextCheckAt">When to ask for them again.</param>
    /// <param name="maxAttempts">How many attempts before announcing it.</param>
    /// <returns>True when it ran out of attempts now.</returns>
    public bool RecordMissingFiles(DocumentFiles? partial, Instant now, Instant nextCheckAt, int maxAttempts)
    {
        DomainGuard.IsFalse(Status == ElectronicDocumentStatus.Validated, Errors.DocumentStatusDoesNotAllowIt);

        Files = partial ?? Files;
        FilesAttempts++;
        LeasedUntil = null;
        LastCheckAt = now;
        UpdatedAt = now;

        if (FilesAttempts < maxAttempts)
        {
            NextCheckAt = nextCheckAt;
            LastCheckOutcome = "files-missing";

            return false;
        }

        FilesStalledAt = now;
        NextCheckAt = null;
        LastCheckOutcome = "files-stalled";

        AddEvent(new ElectronicDocumentFilesStalledDomainEvent(Id, Tenant, IssuerId, Type, Reference, Number?.Prefix, Number?.Consecutive, Cufe, FilesAttempts));

        return true;
    }

    /// <summary>
    /// The provider or the tax authority rejected the document.
    /// </summary>
    /// <param name="origin">Who rejected it.</param>
    /// <param name="reasons">Why, as it came.</param>
    /// <param name="providerDocumentId">The id in the provider, when it got one.</param>
    /// <param name="providerStatus">The status the provider gave, as it came.</param>
    /// <param name="now">The current instant.</param>
    public void Reject(RejectionOrigin origin, IEnumerable<string> reasons, string? providerDocumentId, string? providerStatus, Instant now)
    {
        EnsureNotFinal();

        ProviderDocumentId = providerDocumentId ?? ProviderDocumentId;
        ProviderStatus = providerStatus;
        Rejection = new DocumentRejection(origin, [.. (reasons ?? []).Where(x => !string.IsNullOrWhiteSpace(x))], now);
        Contingency = false;
        Status = ElectronicDocumentStatus.Rejected;
        LastCheckAt = now;
        LastCheckOutcome = "rejected";
        Attempts++;
        NextCheckAt = null;
        LeasedUntil = null;
        StalledAt = null;
        UpdatedAt = now;

        AddEvent(new ElectronicDocumentRejectedDomainEvent(Id, Tenant, IssuerId, Type, Reference, Number?.Prefix, Number?.Consecutive, Rejection, Attempts));
    }

    /// <summary>
    /// The document ran out of retries without a final answer: this service stops insisting until someone retries it.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <returns>False when it was already stalled or it is final, so nothing is written.</returns>
    public bool Stall(Instant now)
    {
        if (StalledAt is not null || IsFinal)
        {
            return false;
        }

        StalledAt = now;
        NextCheckAt = null;
        LeasedUntil = null;
        LastCheckAt = now;
        LastCheckOutcome = "stalled";
        UpdatedAt = now;

        AddEvent(new ElectronicDocumentStalledDomainEvent(Id, Tenant, IssuerId, Type, Reference, Status, Contingency, Attempts, ProviderStatus));

        return true;
    }

    /// <summary>
    /// Replaces the content of a rejected document and sends it again on the same id and the same number.
    /// </summary>
    /// <param name="content">The corrected content.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who corrected it.</param>
    public void Correct(DocumentContent content, Instant now, Guid updatedBy)
    {
        DomainGuard.IsTrue(Status != ElectronicDocumentStatus.Rejected, Errors.OnlyRejectedDocumentsCanBeCorrected);
        DomainGuard.IsNull(content, Errors.ContentIsRequired);

        content.EnsureValid(Type);

        Content = content;
        ContentHash = ContentHashOf(Type, Reference, PrecededBy, content);
        Revision++;
        Status = ElectronicDocumentStatus.Draft;
        Rejection = null;
        ProviderDocumentId = null;
        ProviderStatus = null;
        Attempts = 0;
        NextCheckAt = now;
        LeasedUntil = null;
        StalledAt = null;
        LastCheckOutcome = "corrected";
        UpdatedBy = updatedBy;
        UpdatedAt = now;

        AddEvent(ElectronicDocumentQueuedDomainEvent.Create(Id, Tenant, Type, Reference, PrecededBy));
    }

    /// <summary>
    /// Brings forward the next look at a document still waiting, and restarts its retries when it was stalled. A round
    /// that holds the document keeps it until it ends: bringing it forward never lets a second round in.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="updatedBy">Who asked for it.</param>
    /// <returns>False when the document is final and has its files, so nothing is written.</returns>
    /// <remarks>
    /// A validated document is final, except that its missing files are asked for again: bringing it forward restarts
    /// their retries.
    /// </remarks>
    public bool Expedite(Instant now, Guid updatedBy)
    {
        if (MissingFiles)
        {
            FilesAttempts = 0;
            FilesStalledAt = null;
            NextCheckAt = now;
            UpdatedBy = updatedBy;
            UpdatedAt = now;

            return true;
        }

        if (IsFinal)
        {
            return false;
        }

        if (StalledAt is not null)
        {
            Attempts = 0;
            StalledAt = null;
        }

        NextCheckAt = now;
        UpdatedBy = updatedBy;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Whether the document no longer changes by itself: validated or rejected.
    /// </summary>
    public bool IsFinal => Status is ElectronicDocumentStatus.Validated or ElectronicDocumentStatus.Rejected;

    private void EnsureNotFinal()
    {
        DomainGuard.IsTrue(Status == ElectronicDocumentStatus.Validated, Errors.DocumentAlreadyValidated);
        DomainGuard.IsTrue(Status == ElectronicDocumentStatus.Rejected, Errors.DocumentStatusDoesNotAllowIt);
    }
}
