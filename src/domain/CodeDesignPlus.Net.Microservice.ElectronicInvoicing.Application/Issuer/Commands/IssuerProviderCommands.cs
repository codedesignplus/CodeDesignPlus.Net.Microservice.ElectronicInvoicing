using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Issuers;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Common.Providers;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Issuer.Commands;

/// <summary>
/// Connects the credentials of an issuer whose provider works with each issuer's own account.
/// </summary>
/// <param name="Secrets">The credentials, by the name the adapter gives them.</param>
/// <param name="Settings">What the adapter needs per issuer that is not secret.</param>
public record ConnectIssuerCredentialsCommand(Dictionary<string, string> Secrets, Dictionary<string, string>? Settings) : IRequest;

/// <summary>
/// Validates <see cref="ConnectIssuerCredentialsCommand"/>.
/// </summary>
public class ConnectIssuerCredentialsValidator : AbstractValidator<ConnectIssuerCredentialsCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public ConnectIssuerCredentialsValidator()
    {
        RuleFor(x => x.Secrets).NotEmpty();
        RuleForEach(x => x.Secrets).Must(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value));
    }
}

/// <summary>
/// Handles <see cref="ConnectIssuerCredentialsCommand"/>.
/// </summary>
/// <remarks>
/// The credentials are checked against the provider before they are kept, encrypted with the Vault and bound to the
/// tenant; only the ciphertext and the last four characters of each value are stored. A provider that works with the
/// platform's account takes none.
/// </remarks>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="cipher">The cipher of the Vault.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
public class ConnectIssuerCredentialsCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IIssuerSecretCipher cipher, IPubSub pubsub, IUserContext user) : IRequestHandler<ConnectIssuerCredentialsCommand>
{
    /// <summary>
    /// Checks, seals and keeps the credentials.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(ConnectIssuerCredentialsCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(user.Tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        var provider = factory.Get(issuer.Provider);

        ApplicationGuard.IsTrue(provider.Capabilities.CredentialScope != CredentialScope.PerIssuer, Errors.ProviderNeedsNoCredentials);

        var validation = await provider.ValidateCredentialsAsync(ProviderIssuer.From(issuer, request.Secrets), cancellationToken);

        ApplicationGuard.IsFalse(validation.Accepted, Errors.ProviderCredentialsNotAccepted);

        var ciphertexts = await cipher.SealAsync(user.Tenant, request.Secrets, cancellationToken);
        var hints = request.Secrets.ToDictionary(x => x.Key, x => x.Value.Length <= 4 ? x.Value : x.Value[^4..]);
        var sealedSecrets = new SealedIssuerSecrets(ciphertexts, hints, 0);

        await IssuerWrites.UpdateAsync(issuers, pubsub, user.Tenant, x => x.ConnectCredentials(sealedSecrets, request.Settings ?? [], user.IdUser), cancellationToken);
    }
}

/// <summary>
/// Creates the issuer of the tenant of the user in its provider, when the provider creates issuers through its API.
/// </summary>
/// <param name="FirstName">First names of the person the provider registers as its user.</param>
/// <param name="LastName">Last names of that person.</param>
/// <param name="Email">Email of that person.</param>
/// <param name="Phone">Phone of that person.</param>
public record ProvisionIssuerCommand(string FirstName, string LastName, string Email, string Phone) : IRequest;

/// <summary>
/// Validates <see cref="ProvisionIssuerCommand"/>.
/// </summary>
public class ProvisionIssuerValidator : AbstractValidator<ProvisionIssuerCommand>
{
    /// <summary>
    /// Declares the rules of the command.
    /// </summary>
    public ProvisionIssuerValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(FieldLength.Name);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
    }
}

/// <summary>
/// Handles <see cref="ProvisionIssuerCommand"/>.
/// </summary>
/// <param name="issuers">The repository of the issuers.</param>
/// <param name="factory">The adapters of the providers.</param>
/// <param name="resolver">Prepares the issuer for the adapter.</param>
/// <param name="pubsub">The bus.</param>
/// <param name="user">The context of the user.</param>
public class ProvisionIssuerCommandHandler(IIssuerRepository issuers, IElectronicInvoiceProviderFactory factory, IProviderIssuerResolver resolver, IPubSub pubsub, IUserContext user) : IRequestHandler<ProvisionIssuerCommand>
{
    /// <summary>
    /// Creates the issuer in the provider and keeps its id there.
    /// </summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task Handle(ProvisionIssuerCommand request, CancellationToken cancellationToken)
    {
        ApplicationGuard.IsNull(request, Errors.InvalidRequest);

        var issuer = await issuers.FindAsync<IssuerAggregate>(IssuerAggregate.IdFor(user.Tenant), cancellationToken);

        ApplicationGuard.IsNull(issuer, Errors.IssuerNotFound);

        // Created already: the retry ends well without creating a second one in the provider.
        if (!string.IsNullOrWhiteSpace(issuer.ProviderIssuerId))
        {
            return;
        }

        var provisioning = factory.FindProvisioning(issuer.Provider);

        ApplicationGuard.IsNull(provisioning, Errors.ProviderCannotProvisionIssuers);
        DomainGuard.IsNull(issuer.Mandate, DomainErrors.MandateNotAccepted);

        var provisioned = await provisioning.ProvisionAsync(await resolver.ResolveAsync(issuer, cancellationToken), new IssuerContact(request.FirstName, request.LastName, request.Email, request.Phone), cancellationToken);

        await IssuerWrites.UpdateAsync(issuers, pubsub, user.Tenant, x => x.MarkProvisioned(provisioned.ProviderIssuerId, user.IdUser), cancellationToken);
    }
}
