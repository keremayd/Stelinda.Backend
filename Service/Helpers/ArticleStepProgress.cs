using Microsoft.EntityFrameworkCore;
using Postgres;
using Postgres.Models;
using Service.DataTransferObjects.Responses.ArticlePractice;

namespace Service.Helpers;

/// <summary>Artikel adımının ilerlemesini okur ve doğru cevabı işler.</summary>
public static class ArticleStepProgress
{
    /// <summary>Ünite sırasından ünitenin kimliği; yoksa null.</summary>
    public static Task<int?> UnitIdAsync(
        StelindaContext context,
        int sequence,
        CancellationToken cancellationToken) =>
        context.KartyUnits
            .AsNoTracking()
            .Where(unit => unit.Sequence == sequence)
            .Select(unit => (int?)unit.KartyUnitId)
            .FirstOrDefaultAsync(cancellationToken);

    public static Task<UserKartyArticleStep?> FindAsync(
        StelindaContext context,
        string userId,
        int unitId,
        CancellationToken cancellationToken) =>
        context.UserKartyArticleSteps.FirstOrDefaultAsync(
            step => step.UserId == userId && step.KartyUnitId == unitId,
            cancellationToken);

    /// <summary>
    /// Doğru cevabı adıma sayar; hedefe varınca adım biter. Bitmiş adımda
    /// tur pratik olarak sürer, sayı artmaz.
    /// </summary>
    public static UserKartyArticleStep CountCorrect(
        StelindaContext context,
        UserKartyArticleStep? step,
        string userId,
        int unitId,
        DateTime utcNow)
    {
        if (step is null)
        {
            step = new UserKartyArticleStep { UserId = userId, KartyUnitId = unitId };
            context.UserKartyArticleSteps.Add(step);
        }
        if (step.CompletedAt is not null) return step;

        step.CorrectCount = Math.Min(step.CorrectCount + 1, ArticleStepRules.RoundGoal);
        step.UpdatedAt = utcNow;
        if (step.CorrectCount >= ArticleStepRules.RoundGoal) step.CompletedAt = utcNow;
        return step;
    }

    public static ArticleStepProgressResponse ToResponse(UserKartyArticleStep? step) => new()
    {
        CorrectCount = step?.CorrectCount ?? 0,
        Goal = ArticleStepRules.RoundGoal,
        Completed = step?.CompletedAt is not null,
    };
}
