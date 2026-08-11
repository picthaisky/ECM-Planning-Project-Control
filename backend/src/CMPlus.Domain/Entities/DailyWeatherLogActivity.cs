using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One activity named as affected by a <see cref="DailyWeatherLog"/> entry (domain-rules.md
/// weather-eot §8.1, §3.2) - pure data capture ("which activities did the site engineer tag on
/// this stoppage day"), never an EOT eligibility determination. Whether a tagged activity is
/// actually EOT-eligible depends on criticality plus rules owned entirely by the not-yet-built
/// <c>EotEvaluator</c> (S11-BE-02, out of this task's scope) - this type exists so that evaluator
/// has real data to consume without this task inventing how it decides anything.
///
/// <para><see cref="IAppendOnly"/> - even tighter than <see cref="INeverModified"/>, deliberately:
/// unlike e.g. <see cref="VariationOrderScopeItem"/>, there is no legitimate "clear and re-add"
/// lifecycle here at all, because the owning <see cref="DailyWeatherLog"/> itself is never edited
/// (S11-BE-01 DoD). The constructor is <c>internal</c> so the only way to obtain an instance is
/// through <see cref="DailyWeatherLog"/>'s own factory methods, the same structural-guarantee
/// pattern <see cref="ActivityProgressLog"/> already established.</para>
/// </summary>
public sealed class DailyWeatherLogActivity : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid DailyWeatherLogId { get; private set; }

    public Guid ActivityId { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private DailyWeatherLogActivity()
    {
    }

    internal DailyWeatherLogActivity(Guid tenantId, Guid dailyWeatherLogId, Guid activityId)
    {
        if (dailyWeatherLogId == Guid.Empty)
        {
            throw new DomainException("DailyWeatherLogActivity.DailyWeatherLogId is required.");
        }

        if (activityId == Guid.Empty)
        {
            throw new DomainException("DailyWeatherLogActivity.ActivityId is required.");
        }

        TenantId = tenantId;
        DailyWeatherLogId = dailyWeatherLogId;
        ActivityId = activityId;
    }
}
