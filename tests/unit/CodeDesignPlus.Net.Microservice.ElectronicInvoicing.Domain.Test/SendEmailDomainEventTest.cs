using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Test;

/// <summary>
/// The request of an email travels to ms-emails as JSON: it reads each value by its name, and a name it does not find
/// arrives empty without an error.
/// </summary>
public class SendEmailDomainEventTest
{
    private static readonly Guid FileId = Guid.Parse("cc000000-0000-4000-8000-000000000002");

    private static JObject Serialized() => JObject.Parse(JsonSerializer.Serialize(new SendEmailDomainEvent(
        Guid.NewGuid(),
        "Notice",
        ["comprador@example.com"],
        [],
        [],
        new Dictionary<string, string> { ["subject"] = "Documento de prueba" },
        [new EmailAttachment(FileId, "SETP1.pdf", "electronic-documents")],
        TestData.Tenant)));

    private static string? Read(JToken token, string name) => ((JObject)token).GetValue(name, StringComparison.OrdinalIgnoreCase)?.ToString();

    [Fact]
    public void Serialize_Request_NamesTheTemplateTheRecipientAndTheTenantAsMsEmailsReadsThem()
    {
        // Act
        var json = Serialized();

        // Assert
        Assert.Equal(("Notice", "comprador@example.com", TestData.Tenant.ToString(), "Documento de prueba"), (Read(json, "templateName"), json.GetValue("to", StringComparison.OrdinalIgnoreCase)![0]!.ToString(), Read(json, "tenant"), Read(json.GetValue("variables", StringComparison.OrdinalIgnoreCase)!, "subject")));
    }

    [Fact]
    public void Serialize_Request_NamesEachAttachmentByItsIdItsNameAndItsFolder()
    {
        // Act
        var attachment = Serialized().GetValue("attachments", StringComparison.OrdinalIgnoreCase)![0]!;

        // Assert
        Assert.Equal((FileId.ToString(), "SETP1.pdf", "electronic-documents"), (Read(attachment, "id"), Read(attachment, "name"), Read(attachment, "target")));
    }

    [Fact]
    public void Serialize_Request_CarriesTheListsOfCopiesEvenWhenEmpty()
    {
        // Act
        var json = Serialized();

        // Assert: ms-emails reads them as lists; a missing one would arrive null.
        Assert.Equal((JTokenType.Array, JTokenType.Array), (json.GetValue("cc", StringComparison.OrdinalIgnoreCase)!.Type, json.GetValue("bcc", StringComparison.OrdinalIgnoreCase)!.Type));
    }
}
