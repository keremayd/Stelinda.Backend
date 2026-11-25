using Microsoft.EntityFrameworkCore;
using Postgres;
using Service.DataTransferObjects.Responses.ArticlePractice;

namespace Service.Helpers;

/// <summary>Artikel adımının kelimelerini ve ilerlemesini okur (<see cref="ArticleStepRules"/>).</summary>
public static class ArticleStepProgress
{
    /// <summary>Ünitenin (sırasıyla) Artikel adımında sorulan kelimeleri; ünite yoksa null.</summary>
    public static async Task<IReadOnlyList<int>?> StepWordIdsAsync(
        StelindaContext context,
        int sequence,
        CancellationToken cancellationToken)
    {
        var unitId = await context.KartyUnits
            .AsNoTracking()
            .Where(unit => unit.Sequence == sequence)
            .Select(unit => (int?)unit.KartyUnitId)
            .FirstOrDefaultAsync(cancellationToken);
        if (unitId is null) return null;

        var ordered = await context.Karty
            .AsNoTracking()
            .Where(karty => karty.UnitId == unitId)
            .OrderBy(karty => karty.UnitOrder ?? int.MaxValue)
            .ThenBy(karty => karty.KartyId)
            .Select(karty => karty.KartyId)
            .ToListAsync(cancellationToken);
        return ArticleStepRules.StepWords(ordered);
    }

    /// <summary>Adımın kelimelerinden artikeli en az bir kez doğru bilinenler.</summary>
    public static Task<int> AnsweredCountAsync(
        StelindaContext context,
        string userId,
        IReadOnlyList<int> stepWordIds,
        CancellationToken cancellationToken) =>
        context.UserKartyLearnings
            .AsNoTracking()
            .CountAsync(
                learning => learning.UserId == userId &&
                            stepWordIds.Contains(learning.KartyId) &&
                            learning.LastArticleAnsweredDate != null,
                cancellationToken);

    public static ArticleStepProgressResponse ToResponse(int answered, int goal) => new()
    {
        CorrectCount = Math.Min(answered, goal),
        Goal = goal,
        Completed = goal > 0 && answered >= goal,
    };
}
