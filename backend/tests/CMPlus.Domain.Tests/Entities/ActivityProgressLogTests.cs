using System.Reflection;
using CMPlus.Domain.Entities;

namespace CMPlus.Domain.Tests.Entities;

public class ActivityProgressLogTests
{
    [Fact]
    public void Has_No_Public_Constructor()
    {
        // Immutable BY CONSTRUCTION (S1-BE-02 DoD): the only way to create a row is through
        // Activity.RecordProgress, which lives in the same assembly and can call the internal
        // constructor - Application/Infrastructure code cannot call `new ActivityProgressLog(...)`
        // at all.
        var publicConstructors = typeof(ActivityProgressLog).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Exposes_No_Public_Mutator_Methods()
    {
        // Every public member should be a property getter (get_*) - no Update/Delete/Set method
        // exists anywhere on the type, so append-only is structural, not just a naming convention.
        var publicMethods = typeof(ActivityProgressLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // excludes property get_/set_ accessors
            .ToArray();

        Assert.Empty(publicMethods);

        var propertySetters = typeof(ActivityProgressLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetSetMethod(nonPublic: false))
            .Where(setter => setter is not null);

        Assert.Empty(propertySetters);
    }

    [Fact]
    public void RecordProgress_Is_The_Only_Way_To_Create_An_Entry()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), "A-1", "Name",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 1000m);

        var entry = activity.RecordProgress(
            DateTimeOffset.UtcNow, 10m, null, Guid.NewGuid(),
            CMPlus.Domain.Enums.ProgressSource.Manual, DateTimeOffset.UtcNow);

        Assert.Equal(activity.Id, entry.ActivityId);
        Assert.Equal(activity.TenantId, entry.TenantId);
    }
}
