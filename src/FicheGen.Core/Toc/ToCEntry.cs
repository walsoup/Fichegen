namespace FicheGen.Core.Toc;

public sealed record ToCEntry(
    string Title,
    int PrintedPage,
    int PhysicalPage)
{
    public string DisplayPage => PrintedPage > 0 ? $"p. {PrintedPage}" : PhysicalPage > 0 ? $"p. {PhysicalPage}" : string.Empty;

    public override string ToString() => Title;
}

