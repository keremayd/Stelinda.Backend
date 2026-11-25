using MediatR;
using Microsoft.EntityFrameworkCore;
using Postgres;
using Postgres.Abstractions;
using Postgres.Models;
using Service.DataTransferObjects.Requests.ArticlePractice;
using Service.DataTransferObjects.Responses.ArticlePractice;
using Service.Helpers;

namespace Service.Handlers.Queries.ArticlePractice;

public class GetArticlePracticeTaskQueryHandler
    : IRequestHandler<GetArticlePracticeTaskRequest, GetArticlePracticeTaskResponse>
{
    private const int RequiredWordCount = 2;
    private const int LearningGoal = KartyLeitner.ArticleLearningGoal;
    private readonly StelindaContext _context;

    public GetArticlePracticeTaskQueryHandler(
        IStelindaGenericRepository<UserKartyLearning> learningRepository)
    {
        _context = learningRepository.GetDbContext();
    }

    public async Task<GetArticlePracticeTaskResponse> Handle(
        GetArticlePracticeTaskRequest request,
        CancellationToken cancellationToken)
    {
        await KartyLearningSchemaHelper.EnsureCreatedAsync(_context, cancellationToken);

        // `CorrectCount > 0` şart. Satırın varlığı artık "öğrendi" demiyor:
        // tanışma adımı da satır oluşturuyor (`CorrectCount = 0`). Filtre
        // olmasaydı kullanıcı bir kelimeyi **yalnızca görmüş** olduğu için
        // Artikel Pusulası'nda o kelimeden sorulmaya başlanırdı.
        var learnedWords = await _context.UserKartyLearnings
            .Where(item => item.UserId == request.UserId && item.CorrectCount > 0)
            .Include(item => item.Karty)
            .ToListAsync(cancellationToken);
        if (learnedWords.Count < RequiredWordCount)
        {
            return new GetArticlePracticeTaskResponse
            {
                IsUnlocked = false,
                LearnedWordCount = learnedWords.Count,
                RequiredWordCount = RequiredWordCount,
            };
        }

        var now = DateTime.UtcNow;

        // Patikanın Artikel adımı (ArticleStepRules): ünitenin adımdan önceki
        // kelimelerinin hepsi; araya önceki ünitelerden vadesi gelmiş, en çok
        // yanılınan bir tekrar. Yan durakta (ünitesiz istek) yalnız artikeli
        // henüz 5 kez doğru bilinmemişler sorulurdu.
        ArticleStepProgressResponse? step = null;
        List<UserKartyLearning>? stepPool = null;
        if (request.Unit is { } sequence &&
            await ArticleStepProgress.StepWordIdsAsync(_context, sequence, cancellationToken) is { } stepWordIds)
        {
            var stepSet = stepWordIds.ToHashSet();
            var stepWords = learnedWords.Where(item => stepSet.Contains(item.KartyId)).ToList();
            var pending = stepWords
                .Where(item => !ArticleStepRules.IsAnswered(item.LastArticleAnsweredDate))
                .ToList();
            var reviews = learnedWords
                .Where(item => !stepSet.Contains(item.KartyId) &&
                               ArticleStepRules.IsDue(item.ArticleDueDate, now))
                .ToList();
            var answered = stepWordIds.Count - pending.Count;
            step = ArticleStepProgress.ToResponse(answered, stepWordIds.Count);

            var last = learnedWords
                .Where(item => item.LastArticleServedDate.HasValue)
                .OrderByDescending(item => item.LastArticleServedDate)
                .FirstOrDefault();
            var reviewTurn = pending.Count > 0 && ArticleStepRules.IsReviewTurn(
                answered,
                lastWasStepWord: last is null || stepSet.Contains(last.KartyId),
                hasReviews: reviews.Count > 0);
            stepPool = reviewTurn
                ? reviews
                : pending.Count > 0
                    ? pending
                    : reviews.Count > 0 ? reviews : learnedWords;
        }
        var stepMode = stepPool is not null;

        var activeWords = stepPool ??
                          learnedWords
                              .Where(item => item.ArticleCorrectCount < LearningGoal)
                              .ToList();
        if (activeWords.Count == 0)
        {
            return new GetArticlePracticeTaskResponse
            {
                IsUnlocked = true,
                LearnedWordCount = learnedWords.Count,
                RequiredWordCount = RequiredWordCount,
                Step = step,
            };
        }

        var lastServed = learnedWords
            .Where(item => item.LastArticleServedDate.HasValue)
            .OrderByDescending(item => item.LastArticleServedDate)
            .FirstOrDefault();
        var alternatives = lastServed is null
            ? activeWords
            : activeWords.Where(item => item.KartyId != lastServed.KartyId).ToList();
        if (alternatives.Count == 0)
        {
            alternatives = activeWords;
        }

        var selected = request.PreferredKartyId.HasValue
            ? alternatives.FirstOrDefault(item => item.KartyId == request.PreferredKartyId.Value)
            : null;
        selected ??= stepMode ? SelectForStep(alternatives, now) : SelectNext(alternatives);
        selected.ConsecutiveServeCount = 1;

        var previewCandidates = activeWords
            .Where(item => item.KartyId != selected.KartyId)
            .ToList();
        var preview = previewCandidates.Count == 0
            ? null
            : stepMode
                ? SelectForStep(previewCandidates, now)
                : SelectNext(previewCandidates);

        selected.LastArticleServedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return new GetArticlePracticeTaskResponse
        {
            IsUnlocked = true,
            LearnedWordCount = learnedWords.Count,
            RequiredWordCount = RequiredWordCount,
            Task = ToTask(selected),
            NextTask = preview is null ? null : ToTask(preview),
            Step = step,
        };
    }

    private static UserKartyLearning SelectNext(IEnumerable<UserKartyLearning> candidates)
    {
        return candidates
            .OrderBy(item => Math.Min(item.ArticleCorrectCount, LearningGoal))
            .ThenByDescending(item => item.ArticleAttemptCount - item.ArticleCorrectCount)
            .ThenBy(_ => Random.Shared.NextDouble())
            .First();
    }

    /// <summary>
    /// Artikel adımında havuzdan sıradaki: en çok yanılınan önce, sonra
    /// artikeli en az doğru bilinen, eşitlikte rastgele.
    /// </summary>
    private static UserKartyLearning SelectForStep(
        IEnumerable<UserKartyLearning> candidates,
        DateTime utcNow)
    {
        return candidates
            .OrderBy(item => ArticleStepRules.IsDue(item.ArticleDueDate, utcNow) ? 0 : 1)
            .ThenByDescending(item => item.ArticleAttemptCount - item.ArticleCorrectCount)
            .ThenBy(item => item.ArticleCorrectCount)
            .ThenBy(_ => Random.Shared.NextDouble())
            .First();
    }

    private static ArticlePracticeTaskResponse ToTask(UserKartyLearning learning)
    {
        return new ArticlePracticeTaskResponse
        {
            KartyId = learning.KartyId,
            NounText = learning.Karty!.NounText,
            KartyUrl = learning.Karty.KartyUrl,
            ArticleAttemptCount = learning.ArticleAttemptCount,
            ArticleCorrectCount = learning.ArticleCorrectCount,
            LearningStack = Math.Min(learning.ArticleCorrectCount, LearningGoal),
            LearningGoal = LearningGoal,
        };
    }
}
