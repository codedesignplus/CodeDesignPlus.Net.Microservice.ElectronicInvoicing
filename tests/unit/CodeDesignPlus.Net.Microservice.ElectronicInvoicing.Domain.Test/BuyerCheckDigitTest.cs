namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// The check digit of a buyer, which travels apart from its number: a type that carries one is refused without the digit
/// that belongs to the number, and the other types, the final consumer among them, need none.
/// </summary>
public class BuyerCheckDigitTest
{
    private const string WithCheckDigit = "31";
    private const string WithoutCheckDigit = "13";

    private static readonly CheckDigitRules Rules = TestData.CheckDigitRules();

    public static TheoryData<string, string?, string> RefusedBuyers => new()
    {
        { "901777333", null, Errors.CheckDigitIsMissing.GetCode() },
        { "901777333", "", Errors.CheckDigitIsMissing.GetCode() },
        { "901777333", "  ", Errors.CheckDigitIsMissing.GetCode() },
        { "9018887773", null, Errors.CheckDigitMayBeAttached.GetCode() },
        { "901777333", "5", Errors.CheckDigitDoesNotMatch.GetCode() },
        { "901777333", "10", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "901777333", "A", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "901777333-0", null, Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "901777333-0", "0", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "901.777.333", "0", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "90177733A", "0", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "1234567890123456", "1", Errors.CheckDigitFormatIsInvalid.GetCode() }
    };

    private static DocumentContent Content(string typeCode, string number, string? checkDigit) => TestData.InvoiceContent() with { Buyer = TestData.Buyer() with { IdentificationTypeCode = typeCode, IdentificationNumber = number, CheckDigit = checkDigit } };

    [Theory]
    [MemberData(nameof(RefusedBuyers))]
    public void EnsureSeparate_TypeWithCheckDigitWithoutItsRightDigit_IsRefused(string number, string? checkDigit, string code)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.EnsureSeparate(WithCheckDigit, number, checkDigit));

        // Assert
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData("901777333", "0")]
    [InlineData("901888777", "3")]
    [InlineData("79123456", "0")]
    [InlineData(" 901777333 ", " 0 ")]
    public void EnsureSeparate_NumberWithTheDigitThatBelongsToIt_Passes(string number, string checkDigit)
    {
        // Act
        var exception = Record.Exception(() => Rules.EnsureSeparate(WithCheckDigit, number, checkDigit));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("222222222222", null)]
    [InlineData("79123456", null)]
    [InlineData("AB-123", null)]
    [InlineData("79123456", "9")]
    public void EnsureSeparate_TypeWithoutCheckDigit_PassesWhateverCame(string number, string? checkDigit)
    {
        // Act
        var exception = Record.Exception(() => Rules.EnsureSeparate(WithoutCheckDigit, number, checkDigit));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSeparate_DigitMissing_SaysTheDigitThatBelongs()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.EnsureSeparate(WithCheckDigit, "901777333", null));

        // Assert
        Assert.Equal("Este tipo de identificación se escribe con guion y dígito de verificación. Si 901777333 es el número sin el dígito, escríbelo así: 901777333-0.", exception.Error!.Value.GetMessage("es"));
    }

    [Fact]
    public void EnsureSeparate_DigitMissingAndTheLastOneMayBeIt_GivesBothReadings()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.EnsureSeparate(WithCheckDigit, "9018887773", null));

        // Assert
        Assert.Equal("Este tipo de identificación se escribe con guion y dígito de verificación. Si 9018887773 ya incluye el dígito, escríbelo así: 901888777-3. Si no lo incluye, así: 9018887773-5.", exception.Error!.Value.GetMessage("es"));
    }

    [Fact]
    public void EnsureSeparate_AnotherDigit_SaysTheDigitThatBelongs()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.EnsureSeparate(WithCheckDigit, "901777333", "5"));

        // Assert
        Assert.Equal("El dígito de verificación no corresponde: el de 901777333 es 0, no 5. Escríbelo así: 901777333-0.", exception.Error!.Value.GetMessage("es"));
    }

    [Theory]
    [MemberData(nameof(RefusedBuyers))]
    public void EnsureBuyerCheckDigit_BuyerOfATypeWithCheckDigitWithoutItsRightDigit_IsRefused(string number, string? checkDigit, string code)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Content(WithCheckDigit, number, checkDigit).EnsureBuyerCheckDigit(Rules));

        // Assert
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void EnsureBuyerCheckDigit_BuyerWithItsDigit_Passes()
    {
        // Act
        var exception = Record.Exception(() => Content(WithCheckDigit, "901777333", "0").EnsureBuyerCheckDigit(Rules));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureBuyerCheckDigit_FinalConsumer_NeedsNoDigit()
    {
        // Act
        var exception = Record.Exception(() => Content(WithoutCheckDigit, "222222222222", null).EnsureBuyerCheckDigit(Rules));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureBuyerCheckDigit_DeploymentThatChecksNoBuyer_PassesWithoutTheDigit()
    {
        // Act
        var exception = Record.Exception(() => Content(WithCheckDigit, "901777333", null).EnsureBuyerCheckDigit(CheckDigitRules.None));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureBuyerCheckDigit_WithoutTheRules_ThrowsCheckDigitRulesAreRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Content(WithCheckDigit, "901777333", "0").EnsureBuyerCheckDigit(null!));

        // Assert
        Assert.Equal(Errors.CheckDigitRulesAreRequired.GetCode(), exception.Code);
    }
}
