using Microsoft.EntityFrameworkCore;
using Postgres;
using Postgres.Models;

namespace Service.Helpers;

/// <summary>
/// Doğru cevabın kaydı ve bu cevapla kelimenin pekişip pekişmediği. Kelime
/// yalnız ünitenin pekiştirme oyununda doğru bilinince pekişir
/// (<see cref="UserKartyLearning.ReinforcedAt"/>); pekişme, bir ünitenin
/// hedefe ulaşabileceği tek an.
/// </summary>
public sealed record KartyLearningRecord(UserKartyLearning Learning, bool BecameConsolidated);

public static class KartyLearningRecorder
{
    public static async Task<KartyLearningRecord> RecordAsync(
        StelindaContext context,
        string userId,
        int kartyId,
        CancellationToken cancellationToken,
        bool reinforce = false)
    {
        var learning = await context.UserKartyLearnings
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.KartyId == kartyId,
                cancellationToken);
        var now = DateTime.UtcNow;
        if (learning is null)
        {
            learning = new UserKartyLearning
            {
                UserId = userId,
                KartyId = kartyId,
                CorrectCount = 1,
                Box = 1,
                DueDate = KartyLeitner.NextDue(1, now),
                FirstLearnedDate = now,
                LastCorrectDate = now,
                ReinforcedAt = reinforce ? now : null,
            };
            context.UserKartyLearnings.Add(learning);
            return new KartyLearningRecord(learning, reinforce);
        }

        var wasConsolidated = KartyUnitRules.IsConsolidated(learning.ReinforcedAt);
        if (reinforce && !wasConsolidated) learning.ReinforcedAt = now;
        learning.CorrectCount += 1;
        learning.LastCorrectDate = now;

        // **Kutu yalnız vadesi dolduysa ilerler.** Aynı oturumda ikinci kez
        // doğru bilmek kutuyu ilerletmez; aralıklı tekrarın tamamı bu şarta
        // dayanıyor. Şart kalksaydı kutu, aralığı olmayan bir sayaca — yani
        // düzeltmeye çalıştığımız eski `CorrectCount`'a — geri dönerdi.
        if (KartyLeitner.IsDue(learning, now))
        {
            learning.Box = KartyLeitner.Advance(learning.Box);
            learning.DueDate = KartyLeitner.NextDue(learning.Box, now);
        }

        return new KartyLearningRecord(
            learning,
            !wasConsolidated && KartyUnitRules.IsConsolidated(learning.ReinforcedAt));
    }
}
