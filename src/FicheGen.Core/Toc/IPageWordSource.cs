namespace FicheGen.Core.Toc;

public sealed record PageWord(
    string Text,
    double Bottom,
    double Top,
    double Height,
    double PageWidth,
    double PageHeight);

public interface IPageWordSource
{
    int PageCount { get; }
    IReadOnlyList<PageWord> GetWords(int physicalPage);
}
