namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.ValueObjects;

/// <summary>
/// Which types of identification are written with a hyphen and a check digit, and the weights that digit is computed with.
/// </summary>
/// <remarks>
/// <para>
/// Both lists are configuration of the deployment (<c>ElectronicInvoicing:CheckDigitDocumentTypes</c> and
/// <c>ElectronicInvoicing:CheckDigitWeights</c>), not of this service, which carries no country: the type is a code of the
/// tax authority's list and the weights are the ones that authority fixes. Only the generic computation lives here, a
/// weighted modulus 11: each digit of the number, from right to left, is multiplied by its weight; the sum is divided by
/// 11, and a remainder of 0 or 1 is the digit, otherwise 11 minus the remainder.
/// </para>
/// <para>
/// The number has no fixed length: the tax id of a natural person is its identity card plus the digit. Without configured
/// types, no identification needs a check digit.
/// </para>
/// </remarks>
/// <param name="DocumentTypes">The codes of the types of identification that carry a check digit.</param>
/// <param name="Weights">The weights, from the units digit to the left.</param>
public sealed record CheckDigitRules(IReadOnlyCollection<string> DocumentTypes, IReadOnlyList<int> Weights)
{
    /// <summary>
    /// The divisor of the computation. It is the definition of the modulus 11, not a datum of a country.
    /// </summary>
    private const int Modulus = 11;

    /// <summary>
    /// The rules of a deployment that configures no type with a check digit.
    /// </summary>
    public static readonly CheckDigitRules None = new([], []);

    /// <summary>
    /// Whether a type of identification carries a check digit.
    /// </summary>
    /// <param name="typeCode">The code of the type.</param>
    /// <returns><c>true</c> when it is one of the configured ones.</returns>
    public bool Applies(string? typeCode)
    {
        return !string.IsNullOrWhiteSpace(typeCode) && DocumentTypes.Any(code => string.Equals(code.Trim(), typeCode.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes the check digit of a number.
    /// </summary>
    /// <param name="number">The number, digits only and without its check digit.</param>
    /// <returns>The digit, or <c>null</c> when the number is empty, is not digits only or is longer than the list of weights.</returns>
    public int? Compute(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length > Weights.Count || !number.All(char.IsAsciiDigit))
        {
            return null;
        }

        var sum = 0;

        for (var i = 0; i < number.Length; i++)
        {
            sum += (number[number.Length - 1 - i] - '0') * Weights[i];
        }

        var remainder = sum % Modulus;

        return remainder > 1 ? Modulus - remainder : remainder;
    }

    /// <summary>
    /// Refuses the identification of a type that carries a check digit when it does not come as «number-digit» with the
    /// digit that belongs to the number.
    /// </summary>
    /// <remarks>
    /// The error says what to correct and which digit it is. A number without a hyphen is not guessed to carry the digit
    /// attached, because the number has no fixed length. When its last digit is exactly the check digit of the ones before
    /// it, it may carry it attached or not, and the error gives both writings, the one that separates it first. Otherwise
    /// the error says how it is written if what came is the number without the digit.
    /// </remarks>
    /// <param name="typeCode">The code of the type of identification.</param>
    /// <param name="identification">The identification as it was written.</param>
    /// <exception cref="CodeDesignPlus.Net.Exceptions.CodeDesignPlusException">Thrown with <c>157</c> when the hyphen and the digit are missing, <c>160</c> when they are missing and the last digit may be the check digit, <c>159</c> when the number is not digits only and <c>158</c> when the digit is not the one of the number.</exception>
    public void Ensure(string? typeCode, string? identification)
    {
        if (!Applies(typeCode))
        {
            return;
        }

        var value = identification?.Trim() ?? string.Empty;
        var dash = value.LastIndexOf('-');
        var isMissing = dash < 0;

        var number = isMissing ? value : value[..dash];
        var checkDigit = Compute(number);

        var isWellFormed = checkDigit is not null && (isMissing || (dash == value.Length - 2 && char.IsAsciiDigit(value[^1])));

        DomainGuard.IsFalse(isWellFormed, Errors.CheckDigitFormatIsInvalid);

        var mayBeAttached = isMissing && Compute(number[..^1]) == number[^1] - '0';

        DomainGuard.IsTrue(mayBeAttached, Errors.CheckDigitMayBeAttached.With(number, number[..^1], number[^1], checkDigit));
        DomainGuard.IsTrue(isMissing, Errors.CheckDigitIsMissing.With(number, checkDigit));
        DomainGuard.IsTrue(value[^1] - '0' != checkDigit, Errors.CheckDigitDoesNotMatch.With(number, checkDigit, value[^1]));
    }
}
