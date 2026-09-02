namespace FicheGen.Core.Documents;

public sealed record DocumentLabels(
    string Kicker,
    string Level,
    string Discipline,
    string Duration,
    string Name,
    string FirstName,
    string Class,
    string Date,
    string Grade,
    string Exercise,
    string SkillsHeader,
    string PointsHeader,
    string CorrectionHeading,
    string ExtensionHeading,
    string Ungraded,
    string Total,
    string MissingCorrige);

/// <summary>
/// User-visible chrome labels shared by the HTML renderer and the evaluation
/// assembler, localized per document language (fr-FR / en-US / ar-SA).
/// </summary>
public static class DocumentLabelsResolver
{
    public static DocumentLabels Resolve(string? docType, string? language)
    {
        if (language != null && language.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentLabels(
                Kicker: docType switch
                {
                    "evaluation" => "تقييم",
                    "quiz" => "اختبار قصير",
                    _ => "مذكرة تربوية"
                },
                Level: "المستوى",
                Discipline: "المادة",
                Duration: "المدة",
                Name: "الاسم الكامل",
                FirstName: "الاسم الشخصي",
                Class: "القسم",
                Date: "التاريخ",
                Grade: "النقطة",
                Exercise: "تمرين",
                SkillsHeader: "الكفاءات المستهدفة",
                PointsHeader: "السلم",
                CorrectionHeading: "التصحيح المفصل والسلم (الأستاذ)",
                ExtensionHeading: "للمزيد (غير مندرج في السلم)",
                Ungraded: "غير مندرج في السلم",
                Total: "المجموع",
                MissingCorrige: "(التصحيح غير متوفر لهذا التمرين)");
        }

        if (language != null && language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentLabels(
                Kicker: docType switch
                {
                    "evaluation" => "Evaluation",
                    "quiz" => "Quiz",
                    _ => "Lesson Plan"
                },
                Level: "Level",
                Discipline: "Subject",
                Duration: "Duration",
                Name: "Name",
                FirstName: "First name",
                Class: "Class",
                Date: "Date",
                Grade: "Grade",
                Exercise: "Exercise",
                SkillsHeader: "Assessed skills",
                PointsHeader: "Points",
                CorrectionHeading: "Detailed answer key and marking scheme (teacher)",
                ExtensionHeading: "Going further (ungraded)",
                Ungraded: "ungraded",
                Total: "Total",
                MissingCorrige: "(answer key unavailable for this exercise)");
        }

        return new DocumentLabels(
            Kicker: docType switch
            {
                "evaluation" => "Évaluation",
                "quiz" => "Quiz",
                _ => "Fiche Pédagogique"
            },
            Level: "Niveau",
            Discipline: "Discipline",
            Duration: "Durée",
            Name: "Nom",
            FirstName: "Prénom",
            Class: "Classe",
            Date: "Date",
            Grade: "Note",
            Exercise: "Exercice",
            SkillsHeader: "Compétences évaluées",
            PointsHeader: "Barème",
            CorrectionHeading: "Corrigé détaillé et barème (enseignant)",
            ExtensionHeading: "Pour aller plus loin (non noté)",
            Ungraded: "non noté",
            Total: "Total",
            MissingCorrige: "(corrigé non disponible pour cet exercice)");
    }
}
