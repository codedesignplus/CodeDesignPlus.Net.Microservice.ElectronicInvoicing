using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Services.Aliaddo;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Infrastructure.Test.Services.Aliaddo;

/// <summary>
/// A Nitro that answers what each test says and remembers what it was asked, behind a real <see cref="AliaddoClient"/>.
/// </summary>
public sealed class FakeAliaddo : HttpMessageHandler, IHttpClientFactory
{
    public static readonly Uri BaseUrl = new("https://nitro.example.test");

    private readonly List<(HttpMethod Method, string Path, string? Body, string? Authorization)> requests = [];

    public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> Answer { get; set; } = _ => (HttpStatusCode.OK, "{}");

    public IReadOnlyList<(HttpMethod Method, string Path, string? Body, string? Authorization)> Requests => requests;

    public JsonElement LastBody => JsonDocument.Parse(requests.Last(x => x.Body is not null).Body!).RootElement;

    public static AliaddoOptions Options() => new()
    {
        BaseUrl = BaseUrl,
        Master = new AliaddoMasterOptions { Token = "invented-master-token" },
        LineDiscountName = "Descuento",
        Documents = new()
        {
            ["Invoice"] = new AliaddoDocumentOptions { Code = "01", OperationType = "10", Format = "Standard" },
            ["CreditNote"] = new AliaddoDocumentOptions { Code = "91", OperationType = "20", Format = "Standard" },
            ["DebitNote"] = new AliaddoDocumentOptions { Code = "92", OperationType = "30", Format = "Standard" },
            ["PosTicket"] = new AliaddoDocumentOptions { Code = "20", OperationType = "10", Format = "PosX80mm" }
        }
    };

    public AliaddoProvider Provider(AliaddoOptions? options = null) => new(new AliaddoClient(this, NullLogger<AliaddoClient>.Instance), Microsoft.Extensions.Options.Options.Create(options ?? Options()), NullLogger<AliaddoProvider>.Instance);

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = BaseUrl };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        requests.Add((request.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.ToString()));

        var (status, text) = Answer(request);

        return new HttpResponseMessage(status) { Content = new StringContent(text) };
    }
}
