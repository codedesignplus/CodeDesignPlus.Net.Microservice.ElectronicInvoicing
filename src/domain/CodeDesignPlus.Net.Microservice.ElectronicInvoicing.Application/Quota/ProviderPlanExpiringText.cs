using System.Globalization;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;

/// <summary>
/// What the notice and the email of the end of the contract with the provider say (Kappali pendings/744).
/// </summary>
/// <remarks>
/// <para>
/// The texts are in Spanish, like every notice of the platform, and speak of the contract with the provider of electronic
/// invoicing, with no language of any product, because this service is transversal.
/// </para>
/// <para>
/// The end of the contract is an instant; the date the text shows is its calendar day in the time zone of the plan, the
/// one in which the provider counts its months, and the days left count calendar days in that same zone.
/// </para>
/// </remarks>
public static class ProviderPlanExpiringText
{
    /// <summary>
    /// The title of the notice.
    /// </summary>
    public const string Title = "El contrato con el proveedor de factura electrónica vence pronto";

    /// <summary>
    /// The calendar day on which the contract ends, in the time zone of the plan.
    /// </summary>
    /// <param name="validUntil">When the contract ends.</param>
    /// <param name="zone">The time zone of the plan.</param>
    /// <returns>The day.</returns>
    public static LocalDate ExpiresOn(Instant validUntil, DateTimeZone zone) => validUntil.InZone(zone).Date;

    /// <summary>
    /// The calendar days from the notice to the end of the contract, in the time zone of the plan.
    /// </summary>
    /// <param name="occurredAt">When the notice was raised.</param>
    /// <param name="validUntil">When the contract ends.</param>
    /// <param name="zone">The time zone of the plan.</param>
    /// <returns>The days; zero when it ends the same day.</returns>
    public static int DaysLeft(Instant occurredAt, Instant validUntil, DateTimeZone zone) => Math.Max(0, Period.Between(occurredAt.InZone(zone).Date, ExpiresOn(validUntil, zone), PeriodUnits.Days).Days);

    /// <summary>
    /// The text of the notice.
    /// </summary>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="expiresOn">The calendar day on which the contract ends.</param>
    /// <param name="daysLeft">The calendar days left.</param>
    /// <returns>The text.</returns>
    public static string Message(string provider, LocalDate expiresOn, int daysLeft) => $"El contrato con el proveedor de factura electrónica ({provider}) vence el {Date(expiresOn)}, {In(daysLeft)}. Renuévalo para no interrumpir la emisión de documentos electrónicos.";

    /// <summary>
    /// The subject of the email to the operations mailbox, marked as important: without a contract nothing is issued.
    /// </summary>
    /// <param name="provider">The provider of the plan.</param>
    /// <param name="expiresOn">The calendar day on which the contract ends.</param>
    /// <returns>The subject.</returns>
    public static string EmailSubject(string provider, LocalDate expiresOn) => $"{ProviderPlanThresholdText.ImportantPrefix} El contrato con {provider} vence el {Date(expiresOn)}";

    /// <summary>
    /// A date as the screens show it: «4 de noviembre de 2027».
    /// </summary>
    /// <param name="date">The date.</param>
    /// <returns>The date written.</returns>
    public static string Date(LocalDate date) => $"{date.Day.ToString(CultureInfo.InvariantCulture)} de {ProviderPlanThresholdText.MonthName(date)}";

    private static string In(int daysLeft)
    {
        if (daysLeft == 0)
        {
            return "hoy";
        }

        return daysLeft == 1 ? "en 1 día" : $"en {ProviderPlanThresholdText.Number(daysLeft)} días";
    }
}
