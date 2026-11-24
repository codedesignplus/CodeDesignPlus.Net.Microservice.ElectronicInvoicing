namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// The check digit of an identification: which types carry it, how it is computed and what is refused without it.
/// </summary>
public class CheckDigitRulesTest
{
    private const string WithCheckDigit = "31";
    private const string WithoutCheckDigit = "13";

    private static readonly CheckDigitRules Rules = TestData.CheckDigitRules();

    [Theory]
    [InlineData("901888777", 3)]
    [InlineData("901777333", 0)]
    [InlineData("900000002", 1)]
    [InlineData("900000001", 2)]
    [InlineData("79123456", 0)]
    [InlineData("9018887773", 5)]
    public void Compute_NumberOfAnyLength_IsTheWeightedModulusEleven(string number, int expected)
    {
        // Act
        var digit = Rules.Compute(number);

        // Assert
        Assert.Equal(expected, digit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("90A888777")]
    [InlineData("901.888.777")]
    [InlineData("1234567890123456")]
    public void Compute_NumberThatCannotBeComputed_IsNull(string? number)
    {
        // Act
        var digit = Rules.Compute(number);

        // Assert
        Assert.Null(digit);
    }

    [Theory]
    [InlineData("31", true)]
    [InlineData(" 31 ", true)]
    [InlineData("13", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Applies_TypeCode_OnlyTheConfiguredOnes(string? typeCode, bool expected)
    {
        // Act
        var applies = Rules.Applies(typeCode);

        // Assert
        Assert.Equal(expected, applies);
    }

    [Fact]
    public void Applies_NoTypeConfigured_NoneCarriesACheckDigit()
    {
        // Act
        var applies = CheckDigitRules.None.Applies(WithCheckDigit);

        // Assert
        Assert.False(applies);
    }

    [Theory]
    [InlineData("901888777-3")]
    [InlineData("901777333-0")]
    [InlineData("79123456-0")]
    [InlineData(" 901888777-3 ")]
    public void Ensure_NumberWithItsHyphenAndItsDigit_Passes(string identification)
    {
        // Act
        var exception = Record.Exception(() => Rules.Ensure(WithCheckDigit, identification));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("222222222222")]
    [InlineData("9018887773")]
    [InlineData("AB-123")]
    public void Ensure_TypeWithoutCheckDigit_PassesWhateverTheNumber(string identification)
    {
        // Act
        var exception = Record.Exception(() => Rules.Ensure(WithoutCheckDigit, identification));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Ensure_NumberWithoutHyphen_ThrowsCheckDigitIsMissingWithTheDigit()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.Ensure(WithCheckDigit, "901777333"));

        // Assert
        Assert.Equal(Errors.CheckDigitIsMissing.GetCode(), exception.Code);
        Assert.Equal("Este tipo de identificación se escribe con guion y dígito de verificación. Si 901777333 es el número sin el dígito, escríbelo así: 901777333-0.", exception.Error!.Value.GetMessage("es"));
    }

    [Fact]
    public void Ensure_NumberWithoutHyphenWhoseLastDigitIsTheCheckDigitOfTheRest_GivesBothReadings()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.Ensure(WithCheckDigit, "9018887773"));

        // Assert
        Assert.Equal(Errors.CheckDigitMayBeAttached.GetCode(), exception.Code);
        Assert.Equal("Este tipo de identificación se escribe con guion y dígito de verificación. Si 9018887773 ya incluye el dígito, escríbelo así: 901888777-3. Si no lo incluye, así: 9018887773-5.", exception.Error!.Value.GetMessage("es"));
    }

    [Theory]
    [InlineData("9018887775")]
    [InlineData("3")]
    public void Ensure_LastDigitIsNotTheCheckDigitOfTheRest_DoesNotGuess(string identification)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.Ensure(WithCheckDigit, identification));

        // Assert
        Assert.Equal(Errors.CheckDigitIsMissing.GetCode(), exception.Code);
    }

    [Fact]
    public void Ensure_WrongCheckDigit_ThrowsCheckDigitDoesNotMatchWithTheDigit()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.Ensure(WithCheckDigit, "901888777-5"));

        // Assert
        Assert.Equal(Errors.CheckDigitDoesNotMatch.GetCode(), exception.Code);
        Assert.Equal("El dígito de verificación no corresponde: el de 901888777 es 3, no 5. Escríbelo así: 901888777-3.", exception.Error!.Value.GetMessage("es"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("-3")]
    [InlineData("901888777-")]
    [InlineData("901888777-33")]
    [InlineData("901888777-A")]
    [InlineData("901.888.777-3")]
    [InlineData("901 888 777-3")]
    [InlineData("1234567890123456-1")]
    public void Ensure_NumberThatIsNotDigitsHyphenDigit_ThrowsCheckDigitFormatIsInvalid(string identification)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => Rules.Ensure(WithCheckDigit, identification));

        // Assert
        Assert.Equal(Errors.CheckDigitFormatIsInvalid.GetCode(), exception.Code);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("pt")]
    public void Errors_EveryLanguage_SayTheDigitThatBelongs(string language)
    {
        // Act
        var missing = Errors.CheckDigitIsMissing.With("901888777", 3).GetMessage(language);
        var wrong = Errors.CheckDigitDoesNotMatch.With("901888777", 3, '5').GetMessage(language);
        var attached = Errors.CheckDigitMayBeAttached.With("9018887773", "901888777", '3', 5).GetMessage(language);
        var format = Errors.CheckDigitFormatIsInvalid.GetMessage(language);
        var rules = Errors.CheckDigitRulesAreRequired.GetMessage(language);

        // Assert
        Assert.Contains("901888777-3", missing);
        Assert.Contains("901888777-3", wrong);
        Assert.True(attached.IndexOf("901888777-3", StringComparison.Ordinal) >= 0 && attached.IndexOf("901888777-3", StringComparison.Ordinal) < attached.IndexOf("9018887773-5", StringComparison.Ordinal));
        Assert.DoesNotContain("{", missing + wrong + attached);
        Assert.NotEqual(Errors.CheckDigitFormatIsInvalid.GetCode(), format);
        Assert.NotEqual(Errors.CheckDigitRulesAreRequired.GetCode(), rules);
        Assert.Equal(language == "en", missing == Errors.CheckDigitIsMissing.With("901888777", 3).ToEnglish());
    }
}

/// <summary>
/// The tax id of an issuer of a type that carries a check digit, when it is registered and when it is edited, whatever
/// its provider.
/// </summary>
public class IssuerTaxIdCheckDigitTest
{
    public static TheoryData<string, string> RefusedTaxIds => new()
    {
        { "901777333", Errors.CheckDigitIsMissing.GetCode() },
        { "9018887773", Errors.CheckDigitMayBeAttached.GetCode() },
        { "901888777-5", Errors.CheckDigitDoesNotMatch.GetCode() },
        { "901.888.777-3", Errors.CheckDigitFormatIsInvalid.GetCode() },
        { "90188877A-3", Errors.CheckDigitFormatIsInvalid.GetCode() }
    };

    private static IssuerFiscalData Data(string taxId, string typeCode = "31") => TestData.FiscalData() with { TaxIdTypeCode = typeCode, TaxId = taxId };

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public void Register_TaxIdWithoutItsRightCheckDigit_IsRefused(string taxId, string code)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.Tenant, Data(taxId), "Alanube", TestData.User, TestData.CheckDigitRules()));

        // Assert
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public void Register_WithTheSimulatedProvider_IsRefusedTheSame(string taxId, string code)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.Tenant, Data(taxId), "Simulated", TestData.User, TestData.CheckDigitRules()));

        // Assert
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public void Register_ForThePlatform_IsRefusedTheSame(string taxId, string code)
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.PlatformTenant, Data(taxId), "Alanube", TestData.User, TestData.CheckDigitRules()));

        // Assert
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RefusedTaxIds))]
    public void UpdateFiscalData_TaxIdWithoutItsRightCheckDigit_IsRefusedAndKeepsTheOneItHad(string taxId, string code)
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.UpdateFiscalData(Data(taxId), TestData.User, TestData.CheckDigitRules()));

        // Assert
        Assert.Equal(code, exception.Code);
        Assert.Equal("900000002-1", issuer.TaxId);
    }

    [Theory]
    [InlineData("901777333-0")]
    [InlineData("901888777-3")]
    [InlineData("79123456-0")]
    public void Register_TaxIdWithItsCheckDigit_KeepsItAsWrittenAndSplitsIt(string taxId)
    {
        // Act
        var issuer = IssuerAggregate.Register(TestData.Tenant, Data(taxId), "Simulated", TestData.User, TestData.CheckDigitRules());

        // Assert
        Assert.Equal(taxId, issuer.TaxId);
        Assert.Equal((taxId[..^2], taxId[^1..]), issuer.SplitTaxId());
    }

    [Fact]
    public void UpdateFiscalData_TaxIdWithItsCheckDigit_ReplacesIt()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        // Act
        issuer.UpdateFiscalData(Data("901777333-0"), TestData.User, TestData.CheckDigitRules());

        // Assert
        Assert.Equal("901777333-0", issuer.TaxId);
    }

    [Theory]
    [InlineData("222222222222")]
    [InlineData("79123456")]
    [InlineData("AB12345")]
    public void Register_TypeWithoutCheckDigit_NeedsNone(string taxId)
    {
        // Act
        var issuer = IssuerAggregate.Register(TestData.Tenant, Data(taxId, "13"), "Simulated", TestData.User, TestData.CheckDigitRules());

        // Assert
        Assert.Equal(taxId, issuer.TaxId);
    }

    [Fact]
    public void Register_DeploymentWithoutTypesWithCheckDigit_NeedsNone()
    {
        // Act
        var issuer = IssuerAggregate.Register(TestData.Tenant, Data("901888777"), "Simulated", TestData.User, CheckDigitRules.None);

        // Assert
        Assert.Equal("901888777", issuer.TaxId);
    }

    [Theory]
    [InlineData("31", "901777333-0", true)]
    [InlineData("31", "900559088-2", true)]
    [InlineData("31", " 901777333-0 ", true)]
    [InlineData("31", "901777333", false)]
    [InlineData("31", "9018887773", false)]
    [InlineData("31", "901777333-5", false)]
    [InlineData("31", "901777333-", false)]
    [InlineData("31", "-0", false)]
    [InlineData("31", "901.777.333-0", false)]
    [InlineData("31", "", false)]
    [InlineData("31", null, false)]
    [InlineData("13", "901777333", true)]
    [InlineData("13", "", true)]
    public void IsSatisfiedBy_IdentificationAlreadyKept_SaysWhetherItIsWrittenAsItsTypeAsks(string typeCode, string? identification, bool expected)
    {
        // Act
        var satisfied = TestData.CheckDigitRules().IsSatisfiedBy(typeCode, identification);

        // Assert
        Assert.Equal(expected, satisfied);
    }

    [Theory]
    [InlineData("901777333")]
    [InlineData("9018887773")]
    [InlineData("901777333-5")]
    public void EnsureTaxIdCheckDigit_IssuerKeptWithoutItsRightDigit_ThrowsSayingTheTaxIdItHas(string taxId)
    {
        // Arrange: kept before the rule, when no type was held to its check digit.
        var issuer = IssuerAggregate.Register(TestData.Tenant, Data(taxId), "Simulated", TestData.User, CheckDigitRules.None);

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.EnsureTaxIdCheckDigit(TestData.CheckDigitRules()));

        // Assert
        Assert.Equal(Errors.IssuerTaxIdWithoutItsCheckDigit.GetCode(), exception.Code);
        Assert.Equal($"El emisor está registrado con el identificador tributario {taxId}, sin el guion y el dígito de verificación que le corresponden. Corrígelo en los datos del emisor para poder expedir documentos.", exception.Error!.Value.GetMessage("es"));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("pt")]
    public void EnsureTaxIdCheckDigit_EveryLanguage_SaysTheTaxIdTheIssuerHas(string language)
    {
        // Act
        var message = Errors.IssuerTaxIdWithoutItsCheckDigit.With("901777333").GetMessage(language);

        // Assert
        Assert.Contains("901777333", message);
        Assert.DoesNotContain("{", message);
        Assert.Equal(language == "en", message == Errors.IssuerTaxIdWithoutItsCheckDigit.With("901777333").ToEnglish());
    }

    [Theory]
    [InlineData("901777333-0", "31")]
    [InlineData("79123456", "13")]
    public void EnsureTaxIdCheckDigit_IssuerKeptAsItsTypeAsks_Passes(string taxId, string typeCode)
    {
        // Arrange
        var issuer = IssuerAggregate.Register(TestData.Tenant, Data(taxId, typeCode), "Simulated", TestData.User, TestData.CheckDigitRules());

        // Act
        var exception = Record.Exception(() => issuer.EnsureTaxIdCheckDigit(TestData.CheckDigitRules()));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureTaxIdCheckDigit_WithoutTheRules_ThrowsCheckDigitRulesAreRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => TestData.RegisteredIssuer().EnsureTaxIdCheckDigit(null!));

        // Assert
        Assert.Equal(Errors.CheckDigitRulesAreRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void Register_WithoutTheRules_ThrowsCheckDigitRulesAreRequired()
    {
        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => IssuerAggregate.Register(TestData.Tenant, TestData.FiscalData(), "Simulated", TestData.User, null!));

        // Assert
        Assert.Equal(Errors.CheckDigitRulesAreRequired.GetCode(), exception.Code);
    }

    [Fact]
    public void UpdateFiscalData_WithoutTheRules_ThrowsCheckDigitRulesAreRequired()
    {
        // Arrange
        var issuer = TestData.EnabledIssuer();

        // Act
        var exception = Assert.Throws<CodeDesignPlusException>(() => issuer.UpdateFiscalData(TestData.FiscalData(), TestData.User, null!));

        // Assert
        Assert.Equal(Errors.CheckDigitRulesAreRequired.GetCode(), exception.Code);
    }
}
