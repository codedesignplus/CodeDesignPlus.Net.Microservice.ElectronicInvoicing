using System.Globalization;
using System.Net;
using CodeDesignPlus.Net.ValueObjects.Financial;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

/// <summary>
/// What the notice and the email of a threshold of the plan with the provider say (Kappali pendings/744).
/// </summary>
/// <remarks>
/// <para>
/// The texts are in Spanish, like every notice of the platform: the inbox stores one title and one body, and the frontend
/// paints its own card from the payload when it knows the kind. They speak of the plan and its documents, with no language
/// of any product, because this service is transversal.
/// </para>
/// <para>
/// The worker runs with invariant globalization, so the month and the numbers are written by hand as the screens show
/// them: «noviembre de 2026», «3.000» and «$ 420,00».
/// </para>
/// </remarks>
public static class ProviderPlanThresholdText
{
    /// <summary>
    /// The module that owns the data the notice is about: the plan of the platform with the provider.
    /// </summary>
    public const string Module = "provider-plan";

    /// <summary>
    /// How the subject of an email of an exhausted plan starts, so it stands out in the operations mailbox.
    /// </summary>
    public const string ImportantPrefix = "IMPORTANTE:";

    private static readonly string[] Months = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <summary>
    /// Whether the threshold means that the included documents are used up and every new one is billed as overage.
    /// </summary>
    /// <param name="threshold">The threshold crossed, as a percentage of the included documents.</param>
    /// <returns>True from 100 %.</returns>
    public static bool IsExhausted(int threshold) => threshold >= 100;

    /// <summary>
    /// The percentage of the included documents the month has used, rounded down.
    /// </summary>
    /// <param name="used">Documents of the month.</param>
    /// <param name="documents">Documents the plan includes in the month.</param>
    /// <param name="threshold">The threshold crossed, used when the plan includes no documents.</param>
    /// <returns>The percentage, which may go over 100.</returns>
    /// <remarks>
    /// It is the real one and not the threshold: when a round counts many documents at once, the notice of 70 % may arrive
    /// with the month already at 95 %, and saying 70 % next to «usados 2.850 de 3.000» would contradict itself.
    /// </remarks>
    public static long Percentage(long used, long documents, int threshold) => documents > 0 ? used * 100 / documents : threshold;

    /// <summary>
    /// The title of the notice.
    /// </summary>
    /// <param name="threshold">The threshold crossed.</param>
    /// <returns>The title.</returns>
    public static string Title(int threshold) => IsExhausted(threshold) ? "Bolsa de documentos electrónicos agotada" : $"Bolsa de documentos electrónicos al {threshold} %";

    /// <summary>
    /// The text of the notice.
    /// </summary>
    /// <param name="month">The first day of the month.</param>
    /// <param name="threshold">The threshold crossed.</param>
    /// <param name="used">Documents of the month.</param>
    /// <param name="documents">Documents the plan includes in the month.</param>
    /// <param name="overage">Documents of the month over the included ones.</param>
    /// <param name="estimatedOverageCost">What the overage is estimated to cost, already formatted; only read when the plan is exhausted.</param>
    /// <returns>The text.</returns>
    public static string Message(LocalDate month, int threshold, long used, long documents, long overage, string? estimatedOverageCost)
    {
        var usage = $"La bolsa de documentos electrónicos de {MonthName(month)} va en {Percentage(used, documents, threshold)} % (usados {Number(used)} de {Number(documents)}).";

        if (!IsExhausted(threshold))
        {
            return $"{usage} Los documentos que pasen de {Number(documents)} en el mes se cobran como excedente.";
        }

        return $"{usage} Desde aquí cada documento se cobra como excedente: van {Number(overage)} {(overage == 1 ? "documento" : "documentos")} de excedente, con un costo estimado acumulado de {estimatedOverageCost}.";
    }

    /// <summary>
    /// The subject of the email to the operations mailbox.
    /// </summary>
    /// <param name="month">The first day of the month.</param>
    /// <param name="threshold">The threshold crossed.</param>
    /// <returns>The subject; from 100 % it starts with <see cref="ImportantPrefix"/>.</returns>
    public static string EmailSubject(LocalDate month, int threshold)
    {
        var subject = $"Bolsa de documentos electrónicos de {MonthName(month)} al {threshold} %";

        return IsExhausted(threshold) ? $"{ImportantPrefix} {subject}" : subject;
    }

    /// <summary>
    /// The HTML content of the email: the text of the notice, escaped.
    /// </summary>
    /// <param name="message">The text of the notice.</param>
    /// <returns>The HTML of the content.</returns>
    public static string EmailContent(string message) => $"<p>{WebUtility.HtmlEncode(message)}</p>";

    /// <summary>
    /// A month as the screens show it: «noviembre de 2026».
    /// </summary>
    /// <param name="month">Any day of the month.</param>
    /// <returns>The month and its year.</returns>
    public static string MonthName(LocalDate month) => $"{Months[month.Month - 1]} de {month.Year.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A count with the thousands separated by dots: «3.000».
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>The count written.</returns>
    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

    /// <summary>
    /// An amount as the emails of the platform show it: «$ 1.400,00».
    /// </summary>
    /// <param name="minorUnits">The amount, in minor units of the currency.</param>
    /// <param name="currency">The currency, read from ms-locations.</param>
    /// <returns>The amount written.</returns>
    public static string Amount(long minorUnits, Currency currency)
    {
        var value = Money.FromLong(minorUnits, currency.Code).ToDecimal(currency.DecimalDigits);
        var invariant = Math.Abs(value).ToString($"N{currency.DecimalDigits.ToString(CultureInfo.InvariantCulture)}", CultureInfo.InvariantCulture);
        var digits = string.Concat(invariant.Select(x => x switch { ',' => '.', '.' => ',', _ => x }));
        var sign = value < 0 ? "-" : string.Empty;

        return $"{sign}{currency.Symbol} {digits}";
    }
}
