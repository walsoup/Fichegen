// ============================================================================
//  FicheGen.E2E.Tests — FicheFormDriver
//  Pilote pour les formulaires de génération (Fiche, Évaluation, Quiz).
// ============================================================================

using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Documents;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class FicheFormDriver
{
    private readonly TestEnvironment _env;

    public FicheFormDriver(TestEnvironment env)
    {
        _env = env;
    }

    public void FillFicheForm(string classLevel, string subject, string topic, int durationMinutes, string additionalInstructions = "")
    {
        _env.FicheFormViewModel.ClassLevel = classLevel;
        _env.FicheFormViewModel.Subject = subject;
        _env.FicheFormViewModel.Topic = topic;
        _env.FicheFormViewModel.DurationMinutes = durationMinutes;
        _env.FicheFormViewModel.AdditionalInstructions = additionalInstructions;
    }

    public async Task TriggerGenerationAsync()
    {
        await _env.FicheFormViewModel.GenerateFicheAsync();
    }

    public void CancelGeneration()
    {
        if (_env.FicheFormViewModel.CancelGenerationCommand.CanExecute(null))
        {
            _env.FicheFormViewModel.CancelGeneration();
        }
    }

    public void ResetForm()
    {
        if (_env.FicheFormViewModel.ResetFormCommand.CanExecute(null))
        {
            _env.FicheFormViewModel.ResetForm();
        }
    }

    public GeneratedDocument? CurrentDocument => _env.ResultViewModel.CurrentDocument;
    public string CurrentPreviewHtml => _env.ResultViewModel.PreviewHtml;
    public bool IsGenerating => _env.FicheFormViewModel.IsGenerating;
    public string StatusMessage => _env.FicheFormViewModel.StatusMessage;
    public StatusSeverity StatusSeverity => _env.FicheFormViewModel.CurrentStatusSeverity;
}
