namespace CMPlus.Domain.Enums;

/// <summary>
/// S11-BE-03 (US-11.2): the Issue/Action Log state machine. Strictly linear -
/// <see cref="IssueLog.AdvanceStatus"/> only ever moves one step forward
/// (<see cref="Open"/> -&gt; <see cref="Doing"/> -&gt; <see cref="Closed"/>), matching the
/// prototype's single "next" action button per row (no skip, no reopen - neither is in this
/// sprint's DoD, so neither is built).
/// </summary>
public enum IssueStatus
{
    Open = 1,
    Doing = 2,
    Closed = 3,
}
