using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A construction project owned by a tenant (docs/9 §4, extended by docs/specs/master-plan
/// reconciliation - ADR-0007 EAC fields, domain-decisions.md §2.6 retention/advance fields, all
/// landing in the Sprint 1 migration per docs/10 §3).
/// </summary>
public sealed class Project : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public DateTimeOffset ContractStart { get; private set; }

    public DateTimeOffset ContractFinish { get; private set; }

    public decimal BAC { get; private set; }

    /// <summary>
    /// Nullable so "not yet configured" is distinguishable from a deliberate 0% (S1-BE-01 DoD) -
    /// never defaulted to 0 silently.
    /// </summary>
    public decimal? RetentionRate { get; private set; }

    /// <summary>Nullable for the same reason as <see cref="RetentionRate"/>: null != 0.</summary>
    public decimal? AdvanceRate { get; private set; }

    public DateTimeOffset DataDate { get; private set; }

    // --- EAC (ADR-0007) ---

    /// <summary>Not-null, defaults to <see cref="EacVariant.CpiBased"/> per ADR-0007/docs/10 §3.</summary>
    public EacVariant EacVariantDefault { get; private set; } = EacVariant.CpiBased;

    /// <summary><c>decimal(9,4)</c>, must be &gt; 0 when supplied (ADR-0007).</summary>
    public decimal? EacCustomPerformanceFactor { get; private set; }

    /// <summary><c>decimal(18,2)</c>, must be &gt;= 0 when supplied (ADR-0007).</summary>
    public decimal? EacManualEtc { get; private set; }

    // --- Retention / advance (domain-decisions.md §2, docs/10 §3) ---

    /// <summary>Defaults to <see cref="BAC"/> at construction; retention/advance accrue against
    /// contract value, not budget (domain-decisions.md §2.2).</summary>
    public decimal ContractValue { get; private set; }

    /// <summary>Null = uncapped (Thai-standard contracts); a value = FIDIC-style hard cap.</summary>
    public decimal? RetentionCapPercentage { get; private set; }

    public decimal RetentionRelease1Percentage { get; private set; } = 50.00m;

    public int? DefectsLiabilityMonths { get; private set; }

    public decimal? AdvanceAmountPaid { get; private set; }

    public AdvanceRecoveryMethod AdvanceRecoveryMethod { get; private set; } = AdvanceRecoveryMethod.ProRata;

    /// <summary>Only meaningful when <see cref="AdvanceRecoveryMethod"/> is
    /// <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> (domain-decisions.md §2.3).</summary>
    public decimal? AdvanceRecoveryStartPct { get; private set; }

    public decimal? AdvanceRecoveryRatePct { get; private set; }

    public decimal? AdvanceRecoveryEndPct { get; private set; }

    // EF Core materialization fallback: empirically, EF's ConstructorBindingConvention can refuse
    // to bind an all-caps property name like BAC to a same-named constructor parameter even
    // though ordinary PascalCase properties (Name, Code, Owner, ...) bind correctly - and when the
    // "rich" constructor fails to fully bind, EF does NOT fall back gracefully unless another,
    // fully-bindable constructor (trivially, the parameterless one) also exists; without it, model
    // building throws "No suitable constructor was found". Every entity in this project keeps a
    // private parameterless constructor for this reason, not because it is otherwise needed.
    private Project()
    {
    }

    public Project(
        Guid tenantId,
        string name,
        string code,
        string owner,
        DateTimeOffset contractStart,
        DateTimeOffset contractFinish,
        decimal bac,
        decimal contractValue,
        DateTimeOffset dataDate,
        decimal? retentionRate,
        decimal? advanceRate)
    {
        TenantId = tenantId;
        Name = ValidateRequired(name, nameof(Name));
        Code = ValidateRequired(code, nameof(Code));
        Owner = ValidateRequired(owner, nameof(Owner));
        ContractStart = contractStart;
        ContractFinish = contractFinish;
        BAC = MoneyGuard.EnsureNonNegative(bac, nameof(BAC));
        ContractValue = MoneyGuard.EnsureNonNegative(contractValue, nameof(ContractValue));
        DataDate = dataDate;
        RetentionRate = PercentageGuard.Clamp(retentionRate);
        AdvanceRate = PercentageGuard.Clamp(advanceRate);
    }

    /// <summary>
    /// Convenience factory: defaults <see cref="ContractValue"/> to <paramref name="bac"/> when
    /// not supplied (docs/10 §3: "Project.ContractValue decimal(18,2) (default = BAC)"). Kept as a
    /// static method rather than a constructor overload/default parameter so the entity still has
    /// exactly one constructor for EF Core materialization to bind unambiguously.
    /// </summary>
    public static Project Create(
        Guid tenantId,
        string name,
        string code,
        string owner,
        DateTimeOffset contractStart,
        DateTimeOffset contractFinish,
        decimal bac,
        DateTimeOffset dataDate,
        decimal? retentionRate = null,
        decimal? advanceRate = null,
        decimal? contractValue = null) =>
        new(tenantId, name, code, owner, contractStart, contractFinish, bac,
            contractValue ?? bac, dataDate, retentionRate, advanceRate);

    public void Rename(string name) => Name = ValidateRequired(name, nameof(Name));

    public void SetCode(string code) => Code = ValidateRequired(code, nameof(Code));

    public void SetOwner(string owner) => Owner = ValidateRequired(owner, nameof(Owner));

    /// <summary>
    /// S4-BE-02 (US-4.3): rejects a finish earlier than start as a domain invariant - defense in
    /// depth alongside <c>UpdateProjectCommandValidator</c>'s client-visible FluentValidation rule
    /// ("no silent acceptance of an invalid date range").
    /// </summary>
    public void SetContractDates(DateTimeOffset contractStart, DateTimeOffset contractFinish)
    {
        if (contractFinish < contractStart)
        {
            throw new DomainException("ContractFinish cannot be earlier than ContractStart.");
        }

        ContractStart = contractStart;
        ContractFinish = contractFinish;
    }

    public void SetBac(decimal bac) => BAC = MoneyGuard.EnsureNonNegative(bac, nameof(BAC));

    public void SetDataDate(DateTimeOffset dataDate) => DataDate = dataDate;

    public void SetContractValue(decimal contractValue) =>
        ContractValue = MoneyGuard.EnsureNonNegative(contractValue, nameof(ContractValue));

    public void SetRetentionRate(decimal? retentionRate) => RetentionRate = PercentageGuard.Clamp(retentionRate);

    public void SetAdvanceRate(decimal? advanceRate) => AdvanceRate = PercentageGuard.Clamp(advanceRate);

    /// <summary>Restricted to PM/QS/Executive and audited at the Application layer (ADR-0007(c));
    /// the Domain method itself only enforces the assignment, not the permission check.</summary>
    public void SetEacVariantDefault(EacVariant variant) => EacVariantDefault = variant;

    public void SetEacCustomPerformanceFactor(decimal? performanceFactor)
    {
        if (performanceFactor is <= 0)
        {
            throw new DomainException("EacCustomPerformanceFactor must be greater than zero when supplied.");
        }

        EacCustomPerformanceFactor = performanceFactor;
    }

    public void SetEacManualEtc(decimal? manualEtc)
    {
        if (manualEtc is < 0)
        {
            throw new DomainException("EacManualEtc cannot be negative.");
        }

        EacManualEtc = manualEtc;
    }

    public void SetRetentionCapPercentage(decimal? capPercentage) =>
        RetentionCapPercentage = PercentageGuard.Clamp(capPercentage);

    public void SetRetentionRelease1Percentage(decimal percentage) =>
        RetentionRelease1Percentage = PercentageGuard.Clamp(percentage);

    public void SetDefectsLiabilityMonths(int? months)
    {
        if (months is < 0)
        {
            throw new DomainException("DefectsLiabilityMonths cannot be negative.");
        }

        DefectsLiabilityMonths = months;
    }

    public void SetAdvanceAmountPaid(decimal? amount) =>
        AdvanceAmountPaid = MoneyGuard.EnsureNonNegative(amount, nameof(AdvanceAmountPaid));

    public void SetAdvanceRecoveryMethod(
        AdvanceRecoveryMethod method,
        decimal? startPct = null,
        decimal? ratePct = null,
        decimal? endPct = null)
    {
        AdvanceRecoveryMethod = method;
        AdvanceRecoveryStartPct = PercentageGuard.Clamp(startPct);
        AdvanceRecoveryRatePct = PercentageGuard.Clamp(ratePct);
        AdvanceRecoveryEndPct = PercentageGuard.Clamp(endPct);
    }

    private static string ValidateRequired(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{propertyName} is required.")
            : value.Trim();
}
