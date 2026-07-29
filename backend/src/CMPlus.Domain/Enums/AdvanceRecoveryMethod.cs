namespace CMPlus.Domain.Enums;

/// <summary>
/// How mobilisation-advance recovery is deducted from each payment certificate's period gross
/// amount (domain-decisions.md §2.3, fixtures P1-P7). Backs <c>Project.AdvanceRecoveryMethod</c>.
/// Default is <see cref="ProRata"/>.
/// </summary>
public enum AdvanceRecoveryMethod
{
    /// <summary>Thai practice: deduct a flat percentage of each period's gross, capped by the outstanding balance.</summary>
    ProRata = 1,

    /// <summary>FIDIC 14.2: no recovery below a start threshold, a fixed rate on the excess, forced full recovery past an end threshold.</summary>
    ThresholdBanded = 2,

    /// <summary>QS enters the recovery amount manually each period; still capped by the outstanding balance.</summary>
    Manual = 3,
}
