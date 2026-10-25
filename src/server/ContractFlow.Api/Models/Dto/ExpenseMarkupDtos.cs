using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

/// <summary>One per-category mark-up override on a terms version.</summary>
public record ExpenseMarkupDto(ExpenseCategory Category, decimal Percent);

/// <summary>Per-category mark-up fields of a new terms version.</summary>
public partial class ContractTermsRequest
{
    /// <summary>Overrides of <c>ExpenseMarkupPercent</c> per expense category.</summary>
    public List<ExpenseMarkupDto> ExpenseMarkups { get; set; } = [];

    /// <summary>Null when valid; otherwise the validation message.</summary>
    public string? ExpenseMarkupError() =>
        ExpenseMarkups.Any(m => m.Percent is < 0m or > 1000m)
            ? "Expense mark-up must lie between 0 and 1000 %."
            : ExpenseMarkups.GroupBy(m => m.Category).Any(g => g.Count() > 1)
                ? "Each expense category may have one mark-up."
                : null;

    /// <summary>Copies the per-category overrides onto a new terms version.</summary>
    public void ApplyExpenseMarkups(ContractTerms t) =>
        t.ExpenseMarkups = ExpenseMarkups
            .Select(m => new ExpenseMarkup { Category = m.Category, Percent = m.Percent })
            .ToList();
}
