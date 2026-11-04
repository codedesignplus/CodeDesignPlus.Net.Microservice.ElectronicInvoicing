using CodeDesignPlus.Net.Exceptions;
using CodeDesignPlus.Net.Exceptions.Extensions;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common;

/// <summary>
/// The exceptions of the application layer that are thrown as values (in an expression) instead of through a guard.
/// </summary>
public static class ApplicationErrors
{
    /// <summary>
    /// The exception of an error of the application layer.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The exception, with the message in the language of the request.</returns>
    public static CodeDesignPlusException Of(Error error) => new(Layer.Application, error.GetCode(), error.GetMessage());

    /// <summary>
    /// The issuer has no range that can number the document.
    /// </summary>
    /// <returns>The exception.</returns>
    public static CodeDesignPlusException NoUsableNumberingRange() => Of(Errors.NoUsableNumberingRange);
}
