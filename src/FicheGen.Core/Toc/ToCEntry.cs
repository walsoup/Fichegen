namespace FicheGen.Core.Toc;

public sealed record ToCEntry(
    string Title,
    int PrintedPage,
    int PhysicalPage)
{
}

