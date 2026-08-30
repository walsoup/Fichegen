using System;
using System.Collections.Generic;
using System.Linq;
using FicheGen.App.ViewModels;
using FicheGen.App.Views;
using FicheGen.App.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FicheGen.App;

public sealed partial class MainWindow
{
    // ==========================================================================
    //  Raccourcis clavier — dispatchers
    // ==========================================================================
    private void OnCtrlGInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // Les pages de création gèrent la validation + le focus sur le champ manquant.
        if (ContentFrame.Content is ICreationPage creationPage)
        {
            if (creationPage.TryStartGeneration())
            {
                SetStatus(Services.L10n.Get("Status_Generating"));
            }
            return;
        }

        ShowHint(Services.L10n.Get("Hint_OpenCreationPage_Title"), Services.L10n.Get("Hint_OpenCreationPage_Message"));
    }

    private void OnEscInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // La palette de commandes est prioritaire : Échap la referme d'abord.
        if (CommandPaletteOverlay.IsOpen)
        {
            CommandPaletteOverlay.Close();
            return;
        }

        ShellHintTip.IsOpen = false;

        // Si l'assistant en superposition est ouvert, Échap le referme
        if (AssistantToggleButton.IsChecked == true && ContentFrame.Content is Page page)
        {
            if (page.FindName("Workspace") is Views.Controls.CreationWorkspace ws && ws.AssistantOverlay.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
            {
                AssistantToggleButton.IsChecked = false;
                SetAssistantVisible(false);
                return;
            }
        }

        // Si une génération est en cours, Échap l'annule globalement (F05)
        var resultVm = App.Services.GetService<ResultViewModel>();
        if (resultVm is { IsBusy: true })
        {
            resultVm.CancelActiveOperation();
            SetStatus(Services.L10n.Get("Status_Cancelled"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Cancel_Message"));
            return;
        }

        // En pleine saisie dans un champ éditable, Échap ne doit pas déclencher d'actions globales (F09).
        if (IsFocusInEditableField())
        {
            args.Handled = false;
            return;
        }

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "CancelCommand", "CancelGenerationCommand"))
        {
            SetStatus(Services.L10n.Get("Status_Cancelled"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Cancel_Message"));
            return;
        }

        // À défaut : referme le volet de navigation en mode compact
        if (NavView.IsPaneOpen && NavView.PaneDisplayMode != NavigationViewPaneDisplayMode.Left)
        {
            NavView.IsPaneOpen = false;
        }
    }

    private void OnCtrlBInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AssistantToggleButton.IsChecked = !(AssistantToggleButton.IsChecked == true);
        SetAssistantVisible(AssistantToggleButton.IsChecked == true);
    }

    // ==========================================================================
    //  Palette de commandes (Ctrl+K)
    // ==========================================================================
    private void OnCtrlKInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (CommandPaletteOverlay.IsOpen) CommandPaletteOverlay.Close();
        else OpenCommandPalette();
    }

    public void OpenCommandPalette()
    {
        var L = Services.L10n.Get;
        var actions = new List<CommandPalette.CommandAction>
        {
            new(L("Palette_GotoFiche"), L("Palette_GotoFiche_Sub"), "\uE8A5", "Ctrl+1",
                () => NavigateToTag("FichePage")),
            new(L("Palette_GotoEvaluation"), L("Palette_GotoEvaluation_Sub"), "\uE70F", "Ctrl+2",
                () => NavigateToTag("EvaluationPage")),
            new(L("Palette_GotoQuiz"), L("Palette_GotoQuiz_Sub"), "\uE9D5", "Ctrl+3",
                () => NavigateToTag("QuizPage")),
            new(L("Palette_GotoHistory"), L("Palette_GotoHistory_Sub"), "\uE81C", "Ctrl+4",
                () => NavigateToTag("HistoryPage")),

            new(L("Palette_NewFiche"), L("Palette_NewFiche_Sub"), "\uE710", "Ctrl+N", CreateNewFiche),
            new(L("Palette_ToggleAssistant"), L("Palette_ToggleAssistant_Sub"), "\uE99A", "Ctrl+B",
                () =>
                {
                    AssistantToggleButton.IsChecked = AssistantToggleButton.IsChecked != true;
                    SetAssistantVisible(AssistantToggleButton.IsChecked == true);
                }),
            new(L("Palette_ChangeTheme"), L("Palette_ChangeTheme_Sub"), "\uE793", "", CycleTheme),
            new(L("Palette_SearchDocs"), L("Palette_SearchDocs_Sub"), "\uE721", "Ctrl+F",
                FocusHistorySearch),
            new(L("Palette_OpenSettings"), L("Palette_OpenSettings_Sub"), "\uE713", "Ctrl+,",
                () => NavigateToTag("SettingsPage")),
            new(L("Palette_Shortcuts"), L("Palette_Shortcuts_Sub"), "\uE765", "F1", ShowShortcutsDialog),
        };

        if (ContentFrame.Content is ICreationPage creationPage)
        {
            actions.Insert(4, new CommandPalette.CommandAction(
                L("Palette_GenerateNow"), L("Palette_GenerateNow_Sub"), "\uE768", "Ctrl+G",
                () => creationPage.TryStartGeneration()));
        }

        var resultVm = App.Services.GetService<ResultViewModel>();

        // Génération en cours : proposition d'annulation en tête de liste.
        if (resultVm is { IsBusy: true })
        {
            actions.Insert(0, new CommandPalette.CommandAction(
                L("Palette_StopGeneration"), L("Palette_StopGeneration_Sub"), "\uE71A", "Échap",
                () =>
                {
                    if (ContentFrame.Content is not Page page) return;
                    switch (page.DataContext)
                    {
                        case FicheFormViewModel f: f.CancelGenerationCommand.Execute(null); break;
                        case EvaluationViewModel ev: ev.CancelGenerationCommand.Execute(null); break;
                        case QuizViewModel q: q.CancelGenerationCommand.Execute(null); break;
                    }
                }));
        }

        if (resultVm is { HasDocument: true })
        {
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_ExportPdf"), L("Palette_ExportPdf_Sub"), "\uEA90", "",
                () => resultVm.ExportPdfCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_ExportWord"), L("Palette_ExportWord_Sub"), "\uE8A5", "",
                () => resultVm.ExportDocxCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_PrintDoc"), L("Palette_PrintDoc_Sub"), "\uE749", "Ctrl+P",
                () => resultVm.PrintCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                resultVm.IsStudentView ? L("Palette_ShowTeacherVersion") : L("Palette_ShowStudentVersion"),
                L("Palette_ToggleView_Sub"), "\uE77B", "",
                () => resultVm.ToggleStudentViewCommand.Execute(null)));
        }

        CommandPaletteOverlay.Open(actions);
    }

    private void CycleTheme()
    {
        var next = _shellState.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            "Dark" => "Oled",
            "Oled" => "System",
            _ => "System"
        };
        ApplyTheme(next);
        SetStatus(Services.L10n.Format("Status_Theme", next switch
        {
            "Light" => Services.L10n.Get("Theme_Light") ?? "Clair",
            "Dark" => Services.L10n.Get("Theme_Dark") ?? "Sombre",
            "Oled" => Services.L10n.Get("Theme_Oled") ?? "Sombre OLED",
            _ => Services.L10n.Get("Theme_System") ?? "Système"
        }));
    }

    private void OnCtrlNInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreateNewFiche();
    }

    private void OnCtrlShiftEInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "ExportCommand", "ExportDocxCommand", "ExportPdfCommand", "ExportRtfCommand"))
            {
                SetStatus(Services.L10n.Get("Status_Exporting"));
                ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_Export_Message"));
                return;
            }

            ShowHint(Services.L10n.Get("Hint_Export_Title"), found
                ? Services.L10n.Get("Hint_Unavailable")
                : Services.L10n.Get("Hint_Export_GenerateFirst"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_NothingToExport"));
        }
    }

    private void OnCtrlShiftWInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        var resultVm = App.Services.GetService<ResultViewModel>();
        if (resultVm != null && resultVm.HasDocument)
        {
            _ = resultVm.ExportDocxAsync();
            SetStatus(Services.L10n.Get("Status_Exporting"));
            ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_Export_Message"));
            return;
        }

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "ExportDocxCommand", "ExportCommand", "ExportWordCommand"))
            {
                SetStatus(Services.L10n.Get("Status_Exporting"));
                ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_Export_Message"));
                return;
            }

            ShowHint(Services.L10n.Get("Hint_Export_Title"), found
                ? Services.L10n.Get("Hint_Unavailable")
                : Services.L10n.Get("Hint_Export_GenerateFirst"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_NothingToExport"));
        }
    }

    private void OnCtrlPInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "PrintCommand", "ExportPdfCommand", "PrintPdfCommand"))
            {
                SetStatus(Services.L10n.Get("Status_Printing"));
                ShowHint(Services.L10n.Get("Hint_Print_Title"), Services.L10n.Get("Hint_Print_Message"));
                return;
            }

            ShowHint(Services.L10n.Get("Hint_Print_Title"), found
                ? Services.L10n.Get("Hint_Unavailable")
                : Services.L10n.Get("Hint_Print_GenerateFirst"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Print_Title"), Services.L10n.Get("Hint_NothingToPrint"));
        }
    }

    private void OnCtrlCommaInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateToTag("SettingsPage");
    }

    private void OnCtrlFInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ctrl+F dans un champ de saisie : on ne vole pas le raccourci d'édition.
        if (IsFocusInEditableField()) return;

        args.Handled = true;
        FocusHistorySearch();
    }

    private void OnCtrlZInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ctrl+Z dans un champ de saisie : l'annulation de FRAPPE prime sur celle du document.
        if (IsFocusInEditableField()) return;

        args.Handled = true;

        if (ContentFrame.Content is Views.HistoryPage historyPage)
        {
            historyPage.TriggerUndo();
            return;
        }

        var resultVm = App.Services.GetService<ResultViewModel>();
        if (resultVm != null && resultVm.CanUndo)
        {
            resultVm.Undo();
            SetStatus(Services.L10n.Get("Status_Undone"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Undo_Message"));
            return;
        }

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "UndoCommand", "RestoreLastDeletedCommand", "UndoDeleteCommand"))
        {
            SetStatus(Services.L10n.Get("Status_Undone"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Undo_Message"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_NothingToUndo"));
        }
    }

    private bool IsFocusInEditableField()
    {
        var xamlRoot = Content?.XamlRoot;
        if (xamlRoot == null) return false;

        return FocusManager.GetFocusedElement(xamlRoot) is TextBox or PasswordBox or RichEditBox;
    }

    private void OnCtrlTabInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleNavigation(1);
    }

    private void OnCtrlShiftTabInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleNavigation(-1);
    }

    private void CycleNavigation(int delta)
    {
        var items = NavView.MenuItems;
        if (items.Count == 0) return;

        int current = items.IndexOf(NavView.SelectedItem);
        int next = ((current + delta) % items.Count + items.Count) % items.Count;
        NavView.SelectedItem = items[next];
    }

    private void OnCtrlNumberInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        int index = sender.Key switch
        {
            VirtualKey.Number1 => 0,
            VirtualKey.Number2 => 1,
            VirtualKey.Number3 => 2,
            VirtualKey.Number4 => 3,
            _ => -1
        };

        if (index >= 0 && index < NavView.MenuItems.Count)
        {
            NavView.SelectedItem = NavView.MenuItems[index];
        }
    }

    private void OnF1Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowShortcutsDialog();
    }

    private void OnCtrlZeroInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        (ContentFrame.Content as ICreationPage)?.ResetPreviewZoom();
    }

    // ==========================================================================
    //  Dispatch typé des commandes
    // ==========================================================================
    private static bool ExecuteMatchingCommand(object? viewModel, out bool found, params string[] commandNames)
    {
        found = false;
        if (viewModel is null) return false;

        if (viewModel is FicheFormViewModel fvm)
        {
            if (commandNames.Contains("GenerateFicheCommand") || commandNames.Contains("GenerateCommand"))
            {
                found = true;
                if (fvm.GenerateFicheCommand.CanExecute(null)) { fvm.GenerateFicheCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (fvm.CancelGenerationCommand.CanExecute(null)) { fvm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("ResetFormCommand") || commandNames.Contains("NewFicheCommand"))
            {
                found = true;
                if (fvm.ResetFormCommand.CanExecute(null)) { fvm.ResetFormCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is EvaluationViewModel evm)
        {
            if (commandNames.Contains("GenerateEvaluationCommand") || commandNames.Contains("GenerateCommand") || commandNames.Contains("GenerateFicheCommand"))
            {
                found = true;
                if (evm.GenerateEvaluationCommand.CanExecute(null)) { evm.GenerateEvaluationCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (evm.CancelGenerationCommand.CanExecute(null)) { evm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is QuizViewModel qvm)
        {
            if (commandNames.Contains("GenerateQuizCommand") || commandNames.Contains("GenerateCommand") || commandNames.Contains("GenerateFicheCommand"))
            {
                found = true;
                if (qvm.GenerateQuizCommand.CanExecute(null)) { qvm.GenerateQuizCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (qvm.CancelGenerationCommand.CanExecute(null)) { qvm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is HistoryViewModel)
        {
            // History commands handled directly on page
        }

        var resultVm = App.Services.GetService<ResultViewModel>();
        if (resultVm != null && resultVm.HasDocument)
        {
            if (commandNames.Contains("ExportPdfCommand") || commandNames.Contains("ExportCommand"))
            {
                found = true;
                if (resultVm.ExportPdfCommand.CanExecute(null)) { resultVm.ExportPdfCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("ExportDocxCommand"))
            {
                found = true;
                if (resultVm.ExportDocxCommand.CanExecute(null)) { resultVm.ExportDocxCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("PrintCommand") || commandNames.Contains("PrintPdfCommand"))
            {
                found = true;
                if (resultVm.PrintCommand.CanExecute(null)) { resultVm.PrintCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("UndoCommand") && resultVm.CanUndo)
            {
                found = true;
                resultVm.Undo();
                return true;
            }
        }

        return false;
    }
}
