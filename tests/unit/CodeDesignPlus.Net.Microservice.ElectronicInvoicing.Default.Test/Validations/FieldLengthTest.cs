using CodeDesignPlus.Net.xUnit.Microservice.Validations;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Default.Test.Validations;

/// <summary>
/// The free text fields use the standard lengths of <c>FieldLength</c>.
/// </summary>
public class FieldLengthTest
{
    [Fact]
    public void Validators_FreeTextFields_UseStandardLengths()
    {
        // Act
        var violations = StandardFieldLengths.FindViolations(typeof(Application.Errors).Assembly);

        // Assert
        Assert.Empty(violations);
    }
}
