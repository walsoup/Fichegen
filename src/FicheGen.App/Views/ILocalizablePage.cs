namespace FicheGen.App.Views;

/// <summary>
/// Interface implémentée par les pages capables de rafraîchir dynamiquement leurs chaînes localisées.
/// </summary>
public interface ILocalizablePage
{
    void RefreshLocalizedStrings();
}
