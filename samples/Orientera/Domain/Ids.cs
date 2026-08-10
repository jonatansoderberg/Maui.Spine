namespace Orientera.Domain;

public readonly record struct CompetitionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EventGroupId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PersonId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ResultId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SeriesId(string Value)
{
    public override string ToString() => Value;
}
