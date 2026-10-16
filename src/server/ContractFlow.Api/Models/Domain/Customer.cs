namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// The party that pays MICROC. May differ from the end client in multi-layer
/// chains — see <see cref="EndClient"/>.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Customer number or reference in the customer's own systems.</summary>
    public string? ExternalReference { get; set; }
    /// <summary>VAT/organisation number — needed on invoice exports.</summary>
    public string? VatNumber { get; set; }
    public string? AddressLine { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country code (SE, NO, …).</summary>
    public string? Country { get; set; }
    /// <summary>Contact persons at the customer — JSON-owned list.</summary>
    public List<ContactPerson> Contacts { get; set; } = [];
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<Contract> Contracts { get; set; } = [];
}

/// <summary>A named contact at a customer — orderer, invoice recipient, etc.</summary>
public class ContactPerson
{
    public required string Name { get; set; }
    /// <summary>Free-text role, e.g. "Orderer", "Invoice recipient".</summary>
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
