namespace ContractFlow.Api.Models.Domain;

/// <summary>Mark-up on recharged expenses of one category, overriding the
/// terms' default <see cref="ContractTerms.ExpenseMarkupPercent"/>.</summary>
public class ExpenseMarkup
{
    public ExpenseCategory Category { get; set; }
    /// <summary>Percent added on top of the (converted) expense amount.</summary>
    public decimal Percent { get; set; }
}

public partial class ContractTerms
{
    /// <summary>Per-category mark-up overrides (JSON) — e.g. material 10 %,
    /// travel 0 %; categories not listed take the default percent.</summary>
    public List<ExpenseMarkup> ExpenseMarkups { get; set; } = [];

    /// <summary>The mark-up percent applying to a recharged expense of
    /// <paramref name="category"/>.</summary>
    public decimal MarkupFor(ExpenseCategory category) =>
        ExpenseMarkups.FirstOrDefault(m => m.Category == category)?.Percent ?? ExpenseMarkupPercent;
}
