using FicheGen.Core.Ai;

namespace FicheGen.Core.Prompts;

public sealed record FicheParameters(
    string ClassLevel,
    string Subject,
    string Topic,
    int DurationMinutes = 45,
    string? Instructions = null,
    bool UsePedagogicalGuide = false);

public sealed record EvalParameters(
    string ClassLevel,
    string Subject,
    string Topic,
    string EvalType = "sommative", // "sommative" | "formative"
    int TargetPoints = 20,
    double Difficulty = 0.5,
    string? Instructions = null,
    bool UsePedagogicalGuide = false);

public sealed record QuizParameters(
    string ClassLevel,
    string Subject,
    string Topic,
    int QuestionCount = 5,
    int DurationMinutes = 10,
    bool IncludeQcm = true,
    bool IncludeTrueFalse = false,
    bool IncludeShortAnswer = false,
    string? Instructions = null);

public static class PromptBuilder
{
    public static LlmRequest BuildFichePrompt(FicheParameters p, string? lessonContext = null)
    {
        var systemPrompt = LoadTemplate("fiche_system.txt");

        var userPromptBuilder = new System.Text.StringBuilder();
        userPromptBuilder.AppendLine($"Génère une fiche pédagogique pour le niveau {p.ClassLevel} en {p.Subject}.");
        userPromptBuilder.AppendLine($"Sujet / Leçon : {p.Topic}");
        userPromptBuilder.AppendLine($"Durée prévue : {p.DurationMinutes} minutes.");

        if (!string.IsNullOrWhiteSpace(p.Instructions))
        {
            userPromptBuilder.AppendLine($"Consignes spécifiques : {p.Instructions}");
        }

        if (!string.IsNullOrWhiteSpace(lessonContext))
        {
            userPromptBuilder.AppendLine("\n--- EXTRAIT DU GUIDE PÉDAGOGIQUE OFFICIEL ---");
            userPromptBuilder.AppendLine(lessonContext);
            userPromptBuilder.AppendLine("---------------------------------------------");
            userPromptBuilder.AppendLine("Base-toi sur cet extrait du guide pour structurer les étapes et objectifs.");
        }

        return new LlmRequest(
            Purpose: "fiche",
            SystemPrompt: systemPrompt,
            UserPrompt: userPromptBuilder.ToString(),
            Temperature: 0.7,
            ResponseJson: true
        );
    }

    public static LlmRequest BuildEvalPrompt(EvalParameters p, string? lessonContext = null)
    {
        var systemPrompt = LoadTemplate("eval_system.txt");

        var userPromptBuilder = new System.Text.StringBuilder();
        userPromptBuilder.AppendLine($"Génère une évaluation {p.EvalType} pour le niveau {p.ClassLevel} en {p.Subject}.");
        userPromptBuilder.AppendLine($"Sujet : {p.Topic}");
        userPromptBuilder.AppendLine($"Barème cible : {p.TargetPoints} points au total.");

        if (!string.IsNullOrWhiteSpace(p.Instructions))
        {
            userPromptBuilder.AppendLine($"Consignes spécifiques : {p.Instructions}");
        }

        if (!string.IsNullOrWhiteSpace(lessonContext))
        {
            userPromptBuilder.AppendLine("\n--- EXTRAIT DU GUIDE PÉDAGOGIQUE ---");
            userPromptBuilder.AppendLine(lessonContext);
            userPromptBuilder.AppendLine("------------------------------------");
        }

        return new LlmRequest(
            Purpose: "eval",
            SystemPrompt: systemPrompt,
            UserPrompt: userPromptBuilder.ToString(),
            Temperature: 0.7,
            ResponseJson: true
        );
    }

    public static LlmRequest BuildQuizPrompt(QuizParameters p, string? lessonContext = null)
    {
        var systemPrompt = LoadTemplate("quiz_system.txt");

        var userPromptBuilder = new System.Text.StringBuilder();
        userPromptBuilder.AppendLine($"Génère un quiz de {p.QuestionCount} questions pour le niveau {p.ClassLevel} en {p.Subject}.");
        userPromptBuilder.AppendLine($"Sujet : {p.Topic}");
        userPromptBuilder.AppendLine($"Durée estimée : {p.DurationMinutes} minutes.");

        var types = new List<string>();
        if (p.IncludeQcm) types.Add("QCM (4 options)");
        if (p.IncludeTrueFalse) types.Add("Vrai/Faux");
        if (p.IncludeShortAnswer) types.Add("Réponse courte");

        userPromptBuilder.AppendLine($"Types de questions souhaités : {string.Join(", ", types)}");

        if (!string.IsNullOrWhiteSpace(p.Instructions))
        {
            userPromptBuilder.AppendLine($"Consignes : {p.Instructions}");
        }

        return new LlmRequest(
            Purpose: "quiz",
            SystemPrompt: systemPrompt,
            UserPrompt: userPromptBuilder.ToString(),
            Temperature: 0.7,
            ResponseJson: true
        );
    }

    public static LlmRequest BuildIntentPrompt(string userMessage)
    {
        var systemPrompt = @"Tu es un classificateur d'intention pour un assistant pédagogique.
Analyse le message de l'utilisateur et détermine son intention parmi :
- ""generer"" (si l'utilisateur demande de créer une nouvelle fiche/évaluation/quiz)
- ""modifier"" (si l'utilisateur demande de modifier, corriger, traduire ou adapter le document ouvert)
- ""question"" (si l'utilisateur pose une question sur le contenu du document ou de la pédagogie)

Renvoie un JSON strict : { ""intent"": ""generer|modifier|question"", ""confidence"": 0.95 }";

        return new LlmRequest(
            Purpose: "intent",
            SystemPrompt: systemPrompt,
            UserPrompt: userMessage,
            Temperature: 0.1,
            ResponseJson: true
        );
    }

    public static LlmRequest BuildEditPrompt(string currentDocumentText, string editInstruction)
    {
        var systemPrompt = LoadTemplate("fiche_system.txt");

        var userPrompt = $@"Voici le document actuel au format texte :
--- DOCUMENT ACTUEL ---
{currentDocumentText}
-----------------------

Instruction de modification : {editInstruction}

Règles :
1. Réponds EXCLUSIVEMENT avec le JSON mis à jour du document révisé.
2. Conserve la structure générale tout en appliquant précisément la modification demandée.";

        return new LlmRequest(
            Purpose: "chat",
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Temperature: 0.5,
            ResponseJson: true
        );
    }

    public static LlmRequest BuildQuestionPrompt(string currentDocumentText, string question)
    {
        var systemPrompt = @"Tu es un assistant pédagogique expert pour les enseignants de l'Éducation Nationale.
Réponds à la question de l'enseignant de manière concise, professionnelle et directement utile.";

        var userPrompt = $@"Document de référence actuel :
--- DOCUMENT ---
{currentDocumentText}
----------------

Question de l'enseignant : {question}";

        return new LlmRequest(
            Purpose: "chat",
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            Temperature: 0.7,
            ResponseJson: false
        );
    }

    private static string LoadTemplate(string name)
    {
        var assembly = typeof(PromptBuilder).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        return name switch
        {
            "fiche_system.txt" => "Tu es un expert pédagogique français. Génère une fiche pédagogique au format JSON strict avec metadata et blocks.",
            "eval_system.txt" => "Tu es un concepteur d'évaluations. Génère une évaluation au format JSON avec barème total 20 points.",
            "quiz_system.txt" => "Tu es un concepteur de quiz. Génère un quiz au format JSON avec questions et corrigé.",
            _ => "Génère le document au format JSON strict."
        };
    }
}
