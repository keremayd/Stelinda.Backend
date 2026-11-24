using Microsoft.EntityFrameworkCore;
using Postgres;

namespace Service.Helpers;

/// <summary>
/// Günlük görevler: o gün hangileri verilecek ve ilerleme nerede. Görevler
/// yeni tablo gerektirmez; ilerleme mevcut oyun tablolarından (learning,
/// history, streak, daily activity) İstanbul gününe göre türetilir.
///
/// ## Görev kullanıcının durumuna bakar
///
/// Liste eskiden herkese aynıydı ve bazı görevler o gün yapılamıyordu: bekleyen
/// rövanş kartı yoksa "2 rövanş kartını geri kazan" ancak bilerek yanlış
/// yapılarak biterdi; "3 günlük seriye ulaş" ilk iki gün bitemez, sonrasında
/// oynamadan da tamam görünürdü. Artık her yuvada öncelik sıralı seçenekler
/// var ve o gün yapılabilen ilki verilir. Ödüller yuvaya aittir, seçenek
/// değişince günlük toplam değişmez.
/// </summary>
public static class QuestRules
{
    /// <param name="TitleKey">
    /// Başlığın şablonu (<see cref="QuestTitles"/>). Görev anahtarından ayrı:
    /// aynı şablonu birden çok görev kullanıyor ("{0} kelimeyi doğru bil")
    /// ve hedef sayısı başlığın içinde.
    /// </param>
    public sealed record QuestDefinition(
        string Key,
        string TitleKey,
        int Target,
        int RewardTickets)
    {
        /// <summary>Kullanıcının ana dilinde başlık; şablonu olmayan dilde varsayılan dilde.</summary>
        public string TitleIn(string locale) => QuestTitles.Format(TitleKey, locale, Target);
    }

    /// <summary>Görev seçiminin baktığı durum.</summary>
    /// <param name="NewWords">Bugün tanışılabilecek yeni kelime: açık ünitelerde görülmemişler + bugün tanışılanlar.</param>
    /// <param name="ReachableWords">Bugün doğru bilinebilecek kelime: tanışılanlar + açık ünitelerde görülmemişler.</param>
    /// <param name="ArticleWords">Bugün artikeli sorulabilecek kelime.</param>
    /// <param name="PendingReviews">Günün başında bekleyen rövanş kartı. Bugün yapılan yanlışlar sayılmaz: görev yanlış yaptırmaya çalışmamalı.</param>
    public sealed record QuestDayState(
        int NewWords,
        int ReachableWords,
        int ArticleWords,
        int PendingReviews);

    // Ödüller bilet tavanıyla birlikte ayarlanır (bkz. LivesRules): günde
    // üç görev, her biri 1 bilet (24 Kas 2026, kullanıcı kararı; önceden beş
    // görev, günde 10 bilet). Tavan 5 olduğu için görevler biteni tamamlar,
    // bilet bankası olmaz. "Güne başla" (yalnız açmak) kalktı; "Serini
    // sürdür" de kalktı, yerine "5 kelimeyi doğru bil" (kullanıcı: oynayan
    // herkes seriyi zaten sürdürüyor, görev bir şey istemiyordu).
    private static readonly QuestDefinition CorrectFive = new("correct_five", "correct_n", 5, 1);
    private static readonly QuestDefinition LearnThree = new("learn_three", "learn_n", 3, 1);
    private static readonly QuestDefinition CorrectEight = new("correct_eight", "correct_n", 8, 1);
    private static readonly QuestDefinition ArticleThree = new("article_three", "article_n", 3, 1);
    private static readonly QuestDefinition CorrectTen = new("correct_ten", "correct_n", 10, 1);

    /// <summary>Rövanş görevinin en yüksek hedefi; bekleyen kart azsa onunla sınırlıdır.</summary>
    private const int MaxReviewTarget = 2;

    private static QuestDefinition ReviewQuest(int pendingReviews)
    {
        var target = Math.Clamp(pendingReviews, 1, MaxReviewTarget);
        return new QuestDefinition("review_two", "review_n", target, 1);
    }

    /// <summary>
    /// Günün görevleri, yuva sırasıyla. Ödülü alınmış seçenek durum değişse de
    /// yerinde kalır; hiçbir seçeneği yapılamayan yuva o gün verilmez.
    /// </summary>
    public static IReadOnlyList<QuestDefinition> ChooseForDay(
        QuestDayState day,
        IReadOnlyCollection<string> claimedToday)
    {
        (QuestDefinition Quest, bool Doable)[][] slots =
        [
            [
                (LearnThree, day.NewWords >= LearnThree.Target),
                (CorrectEight, day.ReachableWords >= CorrectEight.Target),
            ],
            [
                (ReviewQuest(day.PendingReviews), day.PendingReviews > 0),
                (ArticleThree, day.ArticleWords >= ArticleThree.Target),
                (CorrectTen, day.ReachableWords >= CorrectTen.Target),
            ],
            [(CorrectFive, day.ReachableWords >= CorrectFive.Target)],
        ];

        var chosen = new List<QuestDefinition>();
        foreach (var slot in slots)
        {
            var claimed = slot.Where(option => claimedToday.Contains(option.Quest.Key));
            var doable = slot.Where(option => option.Doable);
            var pick = claimed.Concat(doable).Select(option => option.Quest).FirstOrDefault();
            if (pick is not null) chosen.Add(pick);
        }

        return chosen;
    }

    public static async Task<QuestDayState> ReadDayStateAsync(
        StelindaContext context,
        string userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = GetDayUtcRange(utcNow);

        var units = await KartyUnitProgressReader.ReadAsync(context, userId, cancellationToken);
        var unseenOpen = units
            .Where(unit => unit.Unlocked)
            .Sum(unit => Math.Max(0, unit.Progress.WordCount - unit.Progress.SeenCount));

        var learnings = await context.UserKartyLearnings
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .Select(l => new
            {
                l.CorrectCount,
                l.ArticleCorrectCount,
                l.FirstLearnedDate,
                l.LastArticleAnsweredDate,
            })
            .ToListAsync(cancellationToken);

        var introducedToday = learnings.Count(
            l => l.FirstLearnedDate >= startUtc && l.FirstLearnedDate < endUtc);
        // Henüz doğru bilinmemiş kelime bugün bilinip Artikel'e düşebilir.
        var articleOpen = learnings.Count(
            l => l.CorrectCount == 0 ||
                 l.ArticleCorrectCount < KartyLeitner.ArticleLearningGoal ||
                 (l.LastArticleAnsweredDate >= startUtc && l.LastArticleAnsweredDate < endUtc));

        // Bugün yeniden yanlış bilinen kartın tarihi ileri gittiği için sayı
        // gün içinde yalnız düşebilir; rövanş hedefi yükselip bitmiş görevi
        // geri almaz.
        var pendingReviews = await context.UserKartyHistories
            .AsNoTracking()
            .CountAsync(
                h => h.UserId == userId &&
                     h.LastWrongDate < startUtc &&
                     (h.ReviewedDate == null || h.ReviewedDate >= startUtc),
                cancellationToken);

        return new QuestDayState(
            NewWords: unseenOpen + introducedToday,
            ReachableWords: learnings.Count + unseenOpen,
            ArticleWords: articleOpen + unseenOpen,
            PendingReviews: pendingReviews);
    }

    // Gün sınırı tanımı `IstanbulDay`'e taşındı: görevlere özel bir kural
    // değil, uygulamanın her yerinde geçerli. Kart zamanlaması da "bugün kaç
    // yeni kelime verildi" diye soruyor; iki ayrı tanım er geç birbirinden
    // ayrılır. Aşağıdakiler mevcut çağıranları kırmamak için duran ince
    // sarmalayıcılar.

    public static DateOnly GetIstanbulDate(DateTime utcNow) =>
        IstanbulDay.DateOf(utcNow);

    public static int GetDayKey(DateTime utcNow) => IstanbulDay.KeyOf(utcNow);

    /// <summary>İstanbul gününün UTC cinsinden [başlangıç, bitiş) aralığı.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) GetDayUtcRange(DateTime utcNow) =>
        IstanbulDay.UtcRangeOf(utcNow);

    public static async Task<int> GetProgressAsync(
        StelindaContext context,
        string userId,
        string questKey,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = GetDayUtcRange(utcNow);

        switch (questKey)
        {
            case "correct_five":
            case "correct_eight":
            case "correct_ten":
                return await context.UserKartyLearnings
                    .AsNoTracking()
                    .CountAsync(
                        l => l.UserId == userId &&
                             l.LastCorrectDate >= startUtc &&
                             l.LastCorrectDate < endUtc,
                        cancellationToken);

            case "learn_three":
                return await context.UserKartyLearnings
                    .AsNoTracking()
                    .CountAsync(
                        l => l.UserId == userId &&
                             l.FirstLearnedDate >= startUtc &&
                             l.FirstLearnedDate < endUtc,
                        cancellationToken);

            case "article_three":
                return await context.UserKartyLearnings
                    .AsNoTracking()
                    .CountAsync(
                        l => l.UserId == userId &&
                             l.LastArticleAnsweredDate >= startUtc &&
                             l.LastArticleAnsweredDate < endUtc,
                        cancellationToken);

            case "review_two":
                return await context.UserKartyHistories
                    .AsNoTracking()
                    .CountAsync(
                        h => h.UserId == userId &&
                             h.ReviewedDate != null &&
                             h.ReviewedDate >= startUtc &&
                             h.ReviewedDate < endUtc,
                        cancellationToken);

            default:
                return 0;
        }
    }
}
