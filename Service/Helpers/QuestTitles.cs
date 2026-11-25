namespace Service.Helpers;

/// <summary>
/// Görev başlıklarının dillere göre şablonları. Başlık sunucuda üretiliyor
/// çünkü hedef sayısı ve hangi seri görevinin seçildiği burada belli; uygulama
/// gelen başlığı olduğu gibi gösterir.
///
/// <c>{0}</c> hedef sayısı. Arnavutçada sayıdan sonra isim çoğul olur
/// ("1 kartë", "2 karta"), İngilizcede de ("1 word", "2 words"); Türkçede olmaz. Bu yüzden şablonun tekil ve
/// çoğul hâli ayrı (<see cref="Template"/>).
///
/// **Arnavutça metinler ana dili Arnavutça olan biri tarafından kontrol
/// edilmeli** (16 Kas 2026, yapay zekâ çevirisi).
/// </summary>
public static class QuestTitles
{
    public sealed record Template(string One, string? Other = null)
    {
        public string For(int count) => count == 1 || Other is null ? One : Other;
    }

    private static readonly Dictionary<string, Dictionary<string, Template>> Templates = new()
    {
        ["correct_n"] = new()
        {
            ["tr"] = new("{0} kelimeyi doğru bil"),
            ["en"] = new("Get {0} word right", "Get {0} words right"),
            ["sq"] = new("Gjej saktë {0} fjalë"),
        },
        ["learn_n"] = new()
        {
            ["tr"] = new("{0} yeni kelime öğren"),
            ["en"] = new("Learn {0} new word", "Learn {0} new words"),
            ["sq"] = new("Mëso {0} fjalë të re", "Mëso {0} fjalë të reja"),
        },
        ["article_n"] = new()
        {
            ["tr"] = new("{0} artikeli doğru bil"),
            ["en"] = new("Get {0} article right", "Get {0} articles right"),
            ["sq"] = new("Gjej saktë {0} artikull", "Gjej saktë {0} artikuj"),
        },
        ["review_n"] = new()
        {
            ["tr"] = new("{0} rövanş kartını geri kazan"),
            ["en"] = new("Win back {0} rematch card", "Win back {0} rematch cards"),
            ["sq"] = new("Fito sërish {0} kartë revanshi", "Fito sërish {0} karta revanshi"),
        },
    };

    public static string Format(string titleKey, string locale, int target)
    {
        var byLocale = Templates[titleKey];
        var template = byLocale.GetValueOrDefault(locale) ?? byLocale[LocaleRules.Default];
        return string.Format(template.For(target), target);
    }
}
