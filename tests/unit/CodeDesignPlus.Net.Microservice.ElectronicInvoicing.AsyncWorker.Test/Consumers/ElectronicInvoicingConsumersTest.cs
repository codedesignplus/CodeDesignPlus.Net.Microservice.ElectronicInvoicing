using CodeDesignPlus.Net.Hangfire.Abstractions.Attributes;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ProcessElectronicDocument;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.ReleaseFollowingDocuments;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Jobs;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.DomainEvents;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Domain.Enums;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Tests.Shared;
using MediatR;
using NodaTime;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.AsyncWorker.Test.Consumers;

public class TenantLicenseUpdatedHandlerTest
{
    [Fact]
    public async Task HandleAsync_License_AppliesItsAttributesUntilItsEnd()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var end = TestData.Now + Duration.FromDays(365);
        var license = new TenantLicense(Guid.NewGuid(), TestData.Now, end, new Dictionary<string, string> { ["einvoicing_documents"] = "600" });
        var data = TenantLicenseUpdatedDomainEvent.Create(TestData.Tenant, license, TestData.User);

        // Act
        await new TenantLicenseUpdatedHandler(mediator.Object, Mock.Of<ILogger<TenantLicenseUpdatedHandler>>()).HandleAsync(data, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<ApplyLicenseQuotaCommand>(c => c.Tenant == TestData.Tenant && c.LicenseEnd == end && c.Attributes["einvoicing_documents"] == "600" && c.PublishedAt == data.OccurredAt), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Deserialize_EventOfTenants_ReadsTheLicenseByName()
    {
        // Arrange: the shape ms-tenants publishes (License with Id, Name, StartDate, EndDate, Modules and Metadata).
        var json = """
        {
          "AggregateId": "aa000000-0000-4000-8000-000000000001",
          "License": { "Id": "aa000000-0000-4000-8000-0000000000aa", "Name": "Esencial", "StartDate": "2026-11-04T00:00:00Z", "EndDate": "2027-11-04T00:00:00Z", "Modules": [], "Metadata": { "einvoicing_documents": "1200" } },
          "UpdatedBy": "aa000000-0000-4000-8000-000000000002",
          "EventId": "aa000000-0000-4000-8000-0000000000bb",
          "OccurredAt": "2026-11-04T15:00:00Z",
          "Metadata": {}
        }
        """;

        // Act
        var data = CodeDesignPlus.Net.Serializers.JsonSerializer.Deserialize<TenantLicenseUpdatedDomainEvent>(json);

        // Assert
        Assert.Equal(("1200", Instant.FromUtc(2027, 11, 4, 0, 0)), (data.License.Metadata["einvoicing_documents"], data.License.EndDate));
    }
}

public class ElectronicDocumentHandlersTest
{
    [Fact]
    public async Task HandleAsync_Queued_RunsTheRoundOfTheDocument()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();

        // Act
        await new ElectronicDocumentQueuedHandler(mediator.Object, Mock.Of<ILogger<ElectronicDocumentQueuedHandler>>()).HandleAsync(ElectronicDocumentQueuedDomainEvent.Create(id, TestData.Tenant, ElectronicDocumentType.Invoice, "orders:order:1", null), CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<ProcessElectronicDocumentCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Validated_ReleasesTheDocumentsThatWaited()
    {
        // Arrange
        var mediator = new Mock<IMediator>();
        var id = Guid.NewGuid();
        var validated = new ElectronicDocumentValidatedDomainEvent(id, TestData.Tenant, Guid.NewGuid(), ElectronicDocumentType.CreditNote, "orders:order:1", "NC", 1, "cude", null, TestData.Now, null, null, TestData.Totals());

        // Act
        await new ElectronicDocumentValidatedHandler(mediator.Object, Mock.Of<ILogger<ElectronicDocumentValidatedHandler>>()).HandleAsync(validated, CancellationToken.None);

        // Assert
        mediator.Verify(x => x.Send(It.Is<ReleaseFollowingDocumentsCommand>(c => c.PrecededBy == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class JobsTest
{
    public static TheoryData<Type> Jobs() => new() { typeof(ElectronicDocumentStatusJob), typeof(NumberingRangeWatchJob), typeof(ProviderPlanWatchJob) };

    [Theory]
    [MemberData(nameof(Jobs))]
    public void Job_IsRecurring_WithItsOwnId(Type job)
    {
        // Act
        var options = job.GetCustomAttribute<RecurringJobOptionsAttribute>();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(options?.JobId));
    }
}
