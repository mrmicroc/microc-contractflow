using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Models.Dto;
using ContractFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var customers = await db.Customers
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSummaryDto(
                c.Id, c.Name, c.ExternalReference, c.VatNumber, c.Country,
                c.IsActive, c.Contracts.Count, c.EndClientContracts.Count))
            .ToListAsync(ct);
        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    [RequireRole(AppRole.Administrator, AppRole.ProjectManager, AppRole.Finance)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers
            .Include(c => c.Contracts).ThenInclude(c => c.TermsVersions)
            .Include(c => c.EndClientContracts).ThenInclude(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(new CustomerDetailDto(
            customer.Id, customer.Name, customer.ExternalReference, customer.VatNumber,
            customer.AddressLine, customer.PostalCode, customer.City, customer.Country,
            customer.Contacts
                .Select(c => new ContactPersonDto(c.Name, c.Role, c.Email, c.Phone))
                .ToList(),
            customer.Notes, customer.IsActive,
            customer.Contracts
                .OrderBy(c => c.Name)
                .Select(c => new ContractSummaryDto(
                    c.Id, c.CustomerId, c.Name, c.ExternalReference,
                    ContractsController.ComponentsOf(c, today),
                    c.StartDate, c.EndDate, c.IsActive))
                .ToList(),
            customer.EndClientContracts
                .OrderBy(c => c.Name)
                .Select(c => new EndClientContractDto(
                    c.Id, c.Name, c.CustomerId, c.Customer.Name, c.EndClientReference,
                    c.StartDate, c.EndDate, c.IsActive))
                .ToList()));
    }

    [HttpPost]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Create(CustomerRequest req, CancellationToken ct)
    {
        var customer = new Customer
        {
            Name = req.Name.Trim(),
            ExternalReference = req.ExternalReference,
            VatNumber = req.VatNumber,
            AddressLine = req.AddressLine,
            PostalCode = req.PostalCode,
            City = req.City,
            Country = req.Country?.ToUpperInvariant(),
            Contacts = ToContacts(req),
            Notes = req.Notes,
            IsActive = req.IsActive,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new CustomerSummaryDto(customer.Id, customer.Name, customer.ExternalReference,
                customer.VatNumber, customer.Country, customer.IsActive, 0));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Update(Guid id, CustomerRequest req, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return NotFound();

        customer.Name = req.Name.Trim();
        customer.ExternalReference = req.ExternalReference;
        customer.VatNumber = req.VatNumber;
        customer.AddressLine = req.AddressLine;
        customer.PostalCode = req.PostalCode;
        customer.City = req.City;
        customer.Country = req.Country?.ToUpperInvariant();
        customer.Contacts = ToContacts(req);
        customer.Notes = req.Notes;
        customer.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(new CustomerSummaryDto(customer.Id, customer.Name,
            customer.ExternalReference, customer.VatNumber, customer.Country,
            customer.IsActive,
            await db.Contracts.CountAsync(c => c.CustomerId == id, ct),
            await db.Contracts.CountAsync(c => c.EndClientId == id, ct)));
    }

    private static List<ContactPerson> ToContacts(CustomerRequest req) =>
        req.Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new ContactPerson
            {
                Name = c.Name.Trim(), Role = c.Role, Email = c.Email, Phone = c.Phone,
            })
            .ToList();

    [HttpDelete("{id:guid}")]
    [RequireRole(AppRole.Administrator)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return NotFound();
        if (await db.Contracts.AnyAsync(c => c.CustomerId == id || c.EndClientId == id, ct))
            return Conflict(new
            {
                error = "hasDependents",
                message = "The organisation is the party or end client on contracts — reassign them first.",
            });

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
