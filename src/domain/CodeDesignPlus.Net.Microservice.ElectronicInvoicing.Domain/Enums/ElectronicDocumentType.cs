namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;

/// <summary>
/// The kind of electronic document an issuer sends to the tax authority.
/// </summary>
/// <remarks>
/// An enum and not a catalog: each kind has its own endpoint, its own document code and its own numbering range in every
/// adapter, and a note has to refer to the document it corrects. A new kind needs that code in each adapter.
/// </remarks>
public enum ElectronicDocumentType
{
    /// <summary>
    /// A sales invoice.
    /// </summary>
    Invoice = 1,

    /// <summary>
    /// A credit note that corrects an invoice downwards.
    /// </summary>
    CreditNote = 2,

    /// <summary>
    /// A debit note that corrects an invoice upwards.
    /// </summary>
    DebitNote = 3,

    /// <summary>
    /// The equivalent document of a point of sale ticket.
    /// </summary>
    PosTicket = 4,

    /// <summary>
    /// The adjustment note that corrects a point of sale ticket: a ticket is not corrected with a credit note.
    /// </summary>
    PosAdjustmentNote = 5
}
