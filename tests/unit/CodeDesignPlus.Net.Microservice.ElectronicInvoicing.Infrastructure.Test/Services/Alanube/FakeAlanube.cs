using System.IO.Compression;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Alanube;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Alanube;

/// <summary>
/// An Alanube that answers what each test says and remembers what it was asked, behind a real <see cref="AlanubeClient"/>.
/// </summary>
/// <remarks>
/// The answers are those the sandbox gave on 2026-11-09, with the signed links of the files replaced by invented ones.
/// </remarks>
public sealed class FakeAlanube : HttpMessageHandler, IHttpClientFactory
{
    public static readonly Uri BaseUrl = new("https://alanube.example.test/e-provider/col/v1/");

    public const string CompanyId = "01M7176VAQHTJVZ83BMY9TSTT8";

    public const string Token = "invented-main-token";

    public const string FilesHost = "https://files.example.test";

    private readonly List<Request> requests = [];

    public sealed record Request(HttpMethod Method, string PathAndQuery, string? Body, string? Authorization, string? IdempotencyKey, string Authority);

    public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> Answer { get; set; } = _ => (HttpStatusCode.OK, "{}");

    public Dictionary<string, byte[]> Files { get; } = [];

    public IReadOnlyList<Request> Requests => requests;

    public IEnumerable<Request> Calls => requests.Where(x => x.Authority != FilesHost);

    public IEnumerable<Request> Downloads => requests.Where(x => x.Authority == FilesHost);

    public JsonElement LastBody => JsonDocument.Parse(requests.Last(x => x.Body is not null).Body!).RootElement;

    public static AlanubeOptions Options() => new()
    {
        BaseUrl = BaseUrl,
        Master = new AlanubeMasterOptions { Token = Token },
        CreditPaymentForm = "2",
        TaxSchemeByRegime = new() { ["48"] = "01", ["49"] = "ZZ" },
        Documents = new()
        {
            ["Invoice"] = new AlanubeDocumentOptions { DocumentType = "01" },
            ["CreditNote"] = new AlanubeDocumentOptions { DocumentType = "91", CorrectedDocumentType = "01" },
            ["DebitNote"] = new AlanubeDocumentOptions { CorrectedDocumentType = "01" },
            ["PosTicket"] = new AlanubeDocumentOptions { DocumentType = "20" }
        },
        ErrorCodes = new AlanubeErrorCodesOptions
        {
            IssuerNotReady = ["AEP1002", "AEP6008", "AEP6012", "AEP15008"],
            AlreadyReceived = ["AEP6009", "AEP6010", "AEP7009", "AEP7010"]
        }
    };

    public AlanubeProvider Provider(AlanubeOptions? options = null) => new(new AlanubeClient(this, NullLogger<AlanubeClient>.Instance), Microsoft.Extensions.Options.Options.Create(options ?? Options()), NullLogger<AlanubeProvider>.Instance);

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = name == AlanubeClient.FilesClientName ? null : BaseUrl };

    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());

                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var key = request.Headers.TryGetValues(AlanubeClient.IdempotencyHeader, out var values) ? values.Single() : null;
        var authority = request.RequestUri!.GetLeftPart(UriPartial.Authority);

        requests.Add(new Request(request.Method, request.RequestUri.PathAndQuery, body, request.Headers.Authorization?.ToString(), key, authority));

        if (authority == FilesHost)
        {
            return Files.TryGetValue(request.RequestUri.AbsolutePath, out var file) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) } : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var (status, text) = Answer(request);

        return new HttpResponseMessage(status) { Content = new StringContent(text) };
    }
}

/// <summary>
/// The answers of the sandbox of Alanube (2026-11-09), as fixtures.
/// </summary>
public static class AlanubeAnswers
{
    public const string FilesBlock = """
        "files": {
          "xml": "https://files.example.test/files/fv.xml?X-Amz-Signature=invented",
          "attachedDocument": "https://files.example.test/files/ad.xml?X-Amz-Signature=invented",
          "zip": "https://files.example.test/files/package.zip?token=invented"
        }
        """;

    public const string Observation = "Regla: FAJ43b, Notificación: Nombre informado No corresponde al registrado en el RUT con respecto al Nit suminstrado.";

    public static string Invoice(string? legalStatus = "ACCEPTED_WITH_OBSERVATIONS", string code = "00", string message = "Procesado Correctamente.", string? errorMessage = Observation, string status = "SENT", string id = "01M717Z8XMRHR2S9Z5Y55NFG8X", long number = 990641077)
    {
        var legal = legalStatus is null ? "null" : $"\"{legalStatus}\"";
        var messages = errorMessage is null ? string.Empty : $"\"{errorMessage}\"";

        return $$"""
            {
              "invoice": {
                "id": "{{id}}",
                "companyIdentification": "900559088",
                "customerIdentification": "900000001",
                "type": "STANDARD",
                "cufe": "31b4253abce02ce00d2474c073bdbbccbe6d93a7ba862bb26f6af5eca2189b0a3961309ca98392dbd6b7bf907dbd1267",
                "date": "2026-11-09T17:48:27-05:00",
                "prefix": "SETP",
                "number": {{number}},
                "fullNumber": "SETP{{number}}",
                "status": "{{status}}",
                "legalStatus": {{legal}},
                "governmentResponse": { "code": "{{code}}", "message": "{{message}}", "errorMessages": [{{messages}}] },
                "xmlFileName": "fv09005590880852600003B9D.xml",
                "zipFileName": "z09005590880852600003E18.zip"
              },
              {{FilesBlock}}
            }
            """;
    }

    public static string RejectedByRule90 => Invoice("REJECTED", "99", "Validación contiene errores en campos mandatorios.", "Regla: 90, Rechazo: Documento procesado anteriormente.", id: "01M717YSMDS1HA3RPT6A36DFE4", number: 991292154);

    public static string Accepted => Invoice("ACCEPTED", errorMessage: null);

    public const string Waiting = """
        { "invoice": { "id": "01M7190000000000000000WAIT", "prefix": "SETP", "number": 990000000, "status": "WAITING_RESPONSE" }, "files": {} }
        """;

    public const string Failed = """
        { "invoice": { "id": "01M7190000000000000000FAIL", "prefix": "SETP", "number": 990000000, "status": "FAILED", "errors": [{ "code": "EPR500", "message": "Communication Error" }] } }
        """;

    public const string PosTicket = """
        {
          "equivalentDocument": {
            "type": { "code": "20", "value": "tiquete de máquina registradora con sistema P.O.S." },
            "id": "01M7180MFFHS1A3E3A6V25XEP8",
            "cude": "a8a5748b2739a10880254e873962aa45f6bead1ac31e4ef56a199c2b85b89f84635c38ec339c87b04b8d0873aeedb371",
            "companyIdentification": "900559088",
            "date": "2026-11-09T17:49:11-05:00",
            "prefix": "EPOS",
            "number": "982584",
            "fullNumber": "EPOS982584",
            "legalStatus": "ACCEPTED",
            "status": "SENT",
            "governmentResponse": { "code": "00", "message": "Procesado Correctamente.", "errorMessages": [] }
          },
          "files": {}
        }
        """;

    public static string List(params string[] documents)
    {
        var inner = documents.Select(x => JsonDocument.Parse(x).RootElement.GetProperty("invoice").GetRawText());

        return $$"""{ "metadata": { "from": "", "to": "", "results_count": {{documents.Length}} }, "invoices": [ {{string.Join(',', inner)}} ] }""";
    }

    public static string Errors(string code, string message) => $$"""{ "errors": [ { "code": "{{code}}", "message": "{{message}}" } ] }""";

    public const string SchemaRefusal = """{"errors":[{"message":"instance requires property \"documentType\""},{"message":"instance requires property \"number\""}]}""";

    public const string Company = """{ "company": { "id": "01M7176VAQHTJVZ83BMY9TSTT8", "name": "Emisor de Prueba SAS", "identification": "900000002", "dv": "1", "type": "associated", "useAlegraCertificate": true, "governmentStatus": { "invoices": "AUTHORIZED", "pos": "AUTHORIZED" }, "notificationByEmail": { "enabled": false }, "webhooks": {} } }""";

    public const string Companies = """{ "metadata": { "from": "", "to": "", "results_count": 2 }, "companies": [ { "id": "01M714NDXKZ5A7N3P53CNTGMSG", "name": "Principal", "identification": "900000002", "dv": "1", "type": "main", "governmentStatus": {} }, { "id": "01M7176VAQHTJVZ83BMY9TSTT8", "name": "Emisor de Prueba SAS", "identification": "900000002", "dv": "1", "type": "associated", "governmentStatus": { "invoices": "AUTHORIZED" } } ] }""";

    public const string NoCompanies = """{ "metadata": { "from": "", "to": "", "results_count": 0 }, "companies": [] }""";
}
