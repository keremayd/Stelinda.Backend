using Postgres.Models;

namespace Service.Helpers;

/// <summary>
/// Patikanın Artikel adımı — **tek doğruluk kaynağı** (24 Kas 2026,
/// kullanıcı onayı; gerekçe ve kaynaklar <c>docs/ARTIKEL_ARASTIRMA.md</c>).
///
/// Her ünitenin ortasında bir Artikel adımı var. Tur ünitenin değil,
/// artikel tekrarı vadesi gelen kelimelerden: artikel bir kez görülüp
/// unutulmasın diye kendi aralıklı takvimi var (yanlış başa döndürür).
/// Tur <see cref="RoundGoal"/> doğru cevapla biter; yarıda bırakılırsa
/// sayı kalır, kaldığı yerden sürer.
/// </summary>
public static class ArticleStepRules
{
    /// <summary>Adımın bitmesi için doğru bilinecek artikel.</summary>
    public const int RoundGoal = 10;

    /// <summary>
    /// Adımın yeri: bu numaralı desteden sonra (ünitenin ortası).
    /// 3 deste → 2'den sonra, 2 deste → 1'den sonra, 1 deste → 1'den sonra.
    /// </summary>
    public static int AfterDeck(int deckCount) => Math.Max(1, (deckCount + 1) / 2);

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
