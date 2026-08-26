using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Globalization;

namespace FicheGen.App.Services;

/// <summary>
/// Traduit les exceptions techniques et erreurs réseau/LLM en messages clairs et bienveillants (FR / EN / AR).
/// </summary>
public static class ErrorMessageTranslator
{
    public static string ToUserFriendlyMessage(Exception? ex)
    {
        var lang = ApplicationLanguages.PrimaryLanguageOverride?.ToLowerInvariant() ?? "fr-fr";
        bool isAr = lang.StartsWith("ar");
        bool isEn = lang.StartsWith("en");

        if (ex is null)
        {
            if (isAr) return "حدث خطأ غير متوقع.";
            if (isEn) return "An unexpected error occurred.";
            return "Une erreur inattendue est survenue.";
        }

        if (ex is OperationCanceledException)
        {
            if (isAr) return "تم إلغاء العملية.";
            if (isEn) return "Operation cancelled.";
            return "Opération annulée.";
        }

        if (ex is IOException ioEx)
        {
            var msg = ioEx.Message.ToLowerInvariant();
            if (msg.Contains("used by another process") || msg.Contains("sharing violation") || msg.Contains("processus") || msg.Contains("verrouillé"))
            {
                if (isAr) return "تعذر الحفظ: الملف مفتوح حالياً في برنامج آخر (Word أو قارئ PDF). يرجى إغلاقه ثم المحاولة مجدداً.";
                if (isEn) return "Cannot save: the file is currently open in Word or another PDF reader. Please close it and try again.";
                return "Impossible d'enregistrer : le fichier est actuellement ouvert dans Word ou votre lecteur PDF. Fermez-le puis réessayez.";
            }
            if (isAr) return "تعذر الوصول إلى الملف المطلوب. يرجى التأكد من أنه غير مفتوح في تطبيق آخر.";
            if (isEn) return "Cannot access the requested file. Please ensure it is not open in another application.";
            return "Impossible d'accéder au fichier demandé. Vérifiez qu'il n'est pas ouvert dans une autre application.";
        }

        if (ex is UnauthorizedAccessException)
        {
            if (isAr) return "تم رفض الوصول إلى المجلد أو مسار الحفظ. يرجى اختيار مجلد حفظ آخر.";
            if (isEn) return "Access denied to target directory. Please choose a different destination folder.";
            return "Accès refusé au dossier ou fichier de destination. Choisissez un autre emplacement d'enregistrement.";
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (isAr)
                {
                    return code switch
                    {
                        400 => "تم رفض الطلب من خدمة ��لذكاء الاصطناعي. يرجى التحقق من صحة المفتاح في الإعدادات.",
                        401 or 403 => "مفتاح الربط غير صالح أو غير مصرح به. يرجى مراجعة مفتاحك في الإعدادات.",
                        429 => "تم بلوغ الحد الأقصى للاستخدام. يرجى الانتظار دقيقة قبل المحاولة من جديد.",
                        >= 500 => "خدمة الذكاء الاصطناعي غير متوفرة مؤقتاً. يرجى المحاولة بعد لحظات.",
                        _ => $"تعذر الاتصال بخدمة الذكاء الاصطناعي (خطأ {code}). تأكد من اتصالك بالإنترنت."
                    };
                }
                if (isEn)
                {
                    return code switch
                    {
                        400 => "Request rejected by AI service. Please check your access key in Settings.",
                        401 or 403 => "Invalid or unauthorized API key. Please check your key in Settings.",
                        429 => "Usage limit reached with AI provider. Please wait a minute before retrying.",
                        >= 500 => "The AI service is temporarily unavailable. Please try again shortly.",
                        _ => $"Cannot connect to AI service (Error {code}). Check your Internet connection."
                    };
                }

                return code switch
                {
                    400 => "Requête refusée par le service IA. Vérifiez que votre clé d'accès est valide dans les Paramètres.",
                    401 or 403 => "Clé de connexion invalide ou non autorisée. Vérifiez votre clé dans les Paramètres.",
                    429 => "Limite d'utilisation atteinte auprès du service IA. Veuillez patienter une minute avant de réessayer.",
                    >= 500 => "Le service d'intelligence artificielle est momentanément indisponible. Réessayez dans quelques instants.",
                    _ => $"Connexion au service IA impossible (erreur {code}). Vérifiez votre connexion Internet."
                };
            }

            var msgHttp = httpEx.Message.ToLowerInvariant();
            if (msgHttp.Contains("host") || msgHttp.Contains("name") || msgHttp.Contains("connect") || msgHttp.Contains("socket"))
            {
                if (isAr) return "تعذر الاتصال بخدمة الذكاء الاصطناعي. تأكد من اتصال الحاسوب بشبكة الإنترنت.";
                if (isEn) return "Cannot contact AI service. Please verify your computer is connected to the Internet.";
                return "Impossible de contacter le service IA. Vérifiez que votre ordinateur est bien connecté à Internet.";
            }

            if (isAr) return "حدث خطأ في الاتصال بخدمة الذكاء الاصطناعي. تأكد من اتصالك بالإنترنت.";
            if (isEn) return "A communication error occurred with the AI service. Check your connection.";
            return "Une erreur de communication avec le service IA est survenue. Vérifiez votre connexion Internet.";
        }

        if (ex is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            if (isAr) return "استغرقت خدمة الذكاء الاصطناعي وقتاً أطول من المتوقع للرد. يرجى إعادة المحاولة.";
            if (isEn) return "The AI service took too long to respond. Please retry the generation.";
            return "Le service IA a mis trop de temps à répondre. Veuillez relancer la génération.";
        }

        if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            if (isAr) return "الملف أو دليل PDF المحدد غير موجود أو يتعذر الوص��ل إليه.";
            if (isEn) return "The selected textbook or PDF file could not be found or accessed.";
            return "Le manuel ou fichier PDF sélectionné est introuvable ou inaccessible.";
        }

        if (ex is JsonException)
        {
            if (isAr) return "ولّد نموذج الذكاء الاصطناعي استجابة غير منسقة. يرجى المحاولة بصياغة أبسط للموضوع.";
            if (isEn) return "The AI model returned an unstructured output. Please retry with a simpler topic description.";
            return "Le modèle d'IA a généré une réponse mal structurée. Réessayez en simplifiant votre sujet.";
        }

        var message = ex.Message;
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
        {
            if (isAr) return "مفتاح الوصول غير صحيح أو غير معروف. توجه إلى الإعدادات للتحقق من بيانات الدخول.";
            if (isEn) return "Invalid or unrecognized API key. Head to Settings to verify your configuration.";
            return "Clé de connexion invalide ou non reconnue. Rendez-vous dans les Paramètres pour vérifier votre configuration.";
        }
        if (message.Contains("429", StringComparison.Ordinal) || message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            if (isAr) return "تم بلوغ الحصة المجانية المؤقتة. يرجى الانتظار قليلاً قبل المحاولة مجدداً.";
            if (isEn) return "AI quota temporarily exhausted. Please wait a brief moment before retrying.";
            return "Quota d'IA temporairement atteint. Veuillez patienter quelques instants avant de réessayer.";
        }

        var sanitized = SanitizeMessage(message);
        if (isAr) return $"حدثت مشكلة: {sanitized}";
        if (isEn) return $"An issue occurred: {sanitized}";
        return $"Une difficulté est survenue : {sanitized}";
    }

    private static string SanitizeMessage(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var sanitized = Regex.Replace(raw, @"(key|api[-_]?key|token)=([^\s&]+)", "$1=[REDACTED]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"(sk-[a-zA-Z0-9_-]{8,})", "sk-[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"(AIza[a-zA-Z0-9_-]{10,})", "AIza[REDACTED]");
        return sanitized;
    }
}
