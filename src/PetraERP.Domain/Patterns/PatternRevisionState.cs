namespace PetraERP.Domain.Patterns;

/// <summary>
/// Lifecycle states of a document pattern revision.
/// Draft -> Validated -> Published -> Retired.
/// Published revisions are immutable; any change produces a new Draft revision.
/// </summary>
public enum PatternRevisionState
{
    Draft = 1,
    Validated = 2,
    Published = 3,
    Retired = 4,
}
