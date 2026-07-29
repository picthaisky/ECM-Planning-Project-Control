using CMPlus.Domain.Enums;

namespace CMPlus.Infrastructure.Persistence.Seed;

/// <summary>Static, fixed seed data for two clearly-distinguishable dev/CI tenants (S1-DB-03).
/// Distinct names, project codes, contract values and dates on purpose - the whole point of this
/// dataset is to give <c>qa-engineer</c>'s Sprint 1 tenant-isolation tests (S1-QA-01) two tenants
/// whose data does not merely differ by <c>TenantId</c>.</summary>
internal static class DevSeedData
{
    public static IReadOnlyList<TenantSeed> Tenants { get; } =
    [
        new TenantSeed(
            Name: "Siam Construction Co., Ltd.",
            EmailDomain: "siam-construction.dev",
            Project: new ProjectSeed(
                Name: "Riverside Condominium Tower B",
                Code: "RCT-B",
                Owner: "Siam Riverside Development PLC",
                ContractStart: new DateTimeOffset(2025, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)),
                ContractFinish: new DateTimeOffset(2027, 3, 31, 0, 0, 0, TimeSpan.FromHours(7)),
                Bac: 850_000_000m,
                DataDate: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
                RetentionRate: 5.00m,
                AdvanceRate: 10.00m)),
        new TenantSeed(
            Name: "Bangkok Infra Builders Ltd.",
            EmailDomain: "bkk-infra.dev",
            Project: new ProjectSeed(
                Name: "Sukhumvit Expressway Extension Package 3",
                Code: "SEE-P3",
                Owner: "Department of Highways",
                ContractStart: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.FromHours(7)),
                ContractFinish: new DateTimeOffset(2029, 6, 30, 0, 0, 0, TimeSpan.FromHours(7)),
                Bac: 2_400_000_000m,
                DataDate: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7)),
                RetentionRate: 10.00m,
                AdvanceRate: 15.00m)),
    ];

    /// <summary>Every <see cref="UserRole"/> value, read via reflection so a seventh/eighth role
    /// added later is picked up automatically rather than silently under-seeded (mirrors the
    /// reflection-based tenant-filter pattern in <c>CmPlusDbContext</c>).</summary>
    public static IReadOnlyList<UserRole> AllRoles { get; } = Enum.GetValues<UserRole>();
}

internal sealed record TenantSeed(string Name, string EmailDomain, ProjectSeed Project);

internal sealed record ProjectSeed(
    string Name,
    string Code,
    string Owner,
    DateTimeOffset ContractStart,
    DateTimeOffset ContractFinish,
    decimal Bac,
    DateTimeOffset DataDate,
    decimal? RetentionRate,
    decimal? AdvanceRate);
