using Postgres.Models;

namespace Service.Helpers;

/// <summary>
/// Patikanın Artikel adımı — **tek doğruluk kaynağı** (24–25 Kas 2026,
/// kullanıcı onayı; gerekçe ve kaynaklar <c>docs/ARTIKEL_ARASTIRMA.md</c>).
///
/// Her ünitenin ortasında bir Artikel adımı var. Adım ünitenin o ana kadarki
/// kelimelerinin (kendinden önceki destelerin) hepsini sorar; her biri bir
/// kez doğru bilinince biter. Araya önceki ünitelerden, artikel tekrarı
/// vadesi gelmiş ve en çok yanılınan kelimeler karışır (her
/// <see cref="ReviewEvery"/> kelimede bir). Tekrarlar kuyruk değil öncelik
/// listesi: seçilmeyen kelime borç olarak birikmez. Önceki kural (bütün
/// öğrenilmişlerden rastgele 10) sorulmamış kelimeyi her ünitede biriktiriyordu
/// (kullanıcı: "yığılma ola ola ilerleyecek").
///
/// İlerleme ayrı tutulmaz, türetilir: ünitenin adımdan önceki kelimelerinden
/// artikeli en az bir kez doğru bilinenler (<c>LastArticleAnsweredDate</c>).
/// Yarıda bırakılan adım bu yüzden kaldığı yerden sürer.
/// </summary>
public static class ArticleStepRules
{
    /// <summary>Her bu kadar ünite kelimesinde bir önceki ünitelerden tekrar.</summary>
    public const int ReviewEvery = 4;

    /// <summary>
    /// Adımın yeri: bu numaralı desteden sonra (ünitenin ortası).
    /// 3 deste → 2'den sonra, 2 deste → 1'den sonra, 1 deste → 1'den sonra.
    /// </summary>
    public static int AfterDeck(int deckCount) => Math.Max(1, (deckCount + 1) / 2);

    /// <summary>Adımın sorduğu kelimeler: adımdan önceki destelerin hepsi.</summary>
    public static IReadOnlyList<T> StepWords<T>(IReadOnlyList<T> orderedUnitCards)
    {
        var decks = KartyDeckRules.Split(orderedUnitCards);
        return decks.Take(AfterDeck(decks.Count)).SelectMany(deck => deck).ToList();
    }

    /// <summary>Kelimenin artikeli bu adımda (ya da önce) en az bir kez doğru bilindi mi.</summary>
    public static bool IsAnswered(DateTime? lastArticleAnsweredDate) => lastArticleAnsweredDate is not null;

    /// <summary>
    /// Sıradaki kart önceki ünitelerden bir tekrar mı: her
    /// <see cref="ReviewEvery"/> ünite kelimesinden sonra bir kez (son
    /// sorulan kart ünite kelimesiyse), tekrar adayı varsa.
    /// </summary>
    public static bool IsReviewTurn(int answeredCount, bool lastWasStepWord, bool hasReviews) =>
        hasReviews && lastWasStepWord && answeredCount % ReviewEvery == ReviewEvery - 1;

    /// <summary>Artikelin tekrar vakti geldi mi; hiç takvime girmemişse geldi.</summary>
    public static bool IsDue(DateTime? articleDueDate, DateTime utcNow) =>
        articleDueDate is null || articleDueDate <= utcNow;

    /// <summary>
    /// Cevabı artikelin takvimine işler. Doğru: vadesi geldiyse bir kademe
    /// ilerler ve vade kelimenin kutu aralıklarıyla uzar (1, 3, 7, 14, 30
    /// gün); vadesi gelmeden doğru bilmek takvimi değiştirmez (aralıklı
    /// tekrar buna dayanıyor). Yanlış: başa döner, hemen yeniden vadesi gelir.
    /// </summary>
    public static void Record(UserKartyLearning learning, bool correct, DateTime utcNow)
    {
        if (!correct)
        {
            learning.ArticleCorrectCount = 0;
            learning.ArticleDueDate = utcNow;
            return;
        }

        if (!IsDue(learning.ArticleDueDate, utcNow)) return;
        learning.ArticleCorrectCount += 1;
        learning.ArticleDueDate = KartyLeitner.NextDue(
            Math.Min(learning.ArticleCorrectCount, KartyLeitner.MaxBox),
            utcNow);
    }
}
