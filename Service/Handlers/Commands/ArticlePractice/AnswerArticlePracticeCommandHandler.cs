using MediatR;
using Microsoft.EntityFrameworkCore;
using Postgres;
using Postgres.Abstractions;
using Postgres.Models;
using Service.DataTransferObjects.Requests.ArticlePractice;
using Service.DataTransferObjects.Responses.ArticlePractice;
using Service.Helpers;

namespace Service.Handlers.Commands.ArticlePractice;

public class AnswerArticlePracticeCommandHandler
    : IRequestHandler<AnswerArticlePracticeRequest, AnswerArticlePracticeResponse>
{
    private readonly StelindaContext _context;

    public AnswerArticlePracticeCommandHandler(
        IStelindaGenericRepository<UserKartyLearning> learningRepository)
    {
        _context = learningRepository.GetDbContext();
    }

    public async Task<AnswerArticlePracticeResponse> Handle(
        AnswerArticlePracticeRequest request,
        CancellationToken cancellationToken)
    {
        await KartyLearningSchemaHelper.EnsureCreatedAsync(_context, cancellationToken);

        var learning = await _context.UserKartyLearnings
            .Include(item => item.Karty)
            .FirstOrDefaultAsync(
                item => item.UserId == request.UserId && item.KartyId == request.KartyId,
                cancellationToken);
        var lastServed = await _context.UserKartyLearnings
            .Where(item => item.UserId == request.UserId && item.LastArticleServedDate.HasValue)
            .OrderByDescending(item => item.LastArticleServedDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (learning?.Karty is null || lastServed?.KartyId != request.KartyId)
        {
            return new AnswerArticlePracticeResponse { IsAccepted = false };
        }

        if (learning.LastArticleAnsweredDate.HasValue &&
            learning.LastArticleServedDate.HasValue &&
            learning.LastArticleAnsweredDate >= learning.LastArticleServedDate)
        {
            return new AnswerArticlePracticeResponse { IsAccepted = false };
        }

        var selectedArticle = request.SelectedArticle.Trim().ToLowerInvariant();
        var correctArticle = learning.Karty.Article.Trim().ToLowerInvariant();
        var isCorrect = selectedArticle == correctArticle;
        var now = DateTime.UtcNow;
        learning.ArticleAttemptCount += 1;

        // Artikel bilgisi ayrı ilerler, kendi takvimiyle (ArticleStepRules):
        // doğru vadesi geldiyse aralığı uzatır, yanlış başa döndürür.
        ArticleStepRules.Record(learning, isCorrect, now);
        if (isCorrect) learning.LastArticleAnsweredDate = now;

        // Patikanın Artikel adımı: doğru cevap adımın ilerlemesine sayılır.
        UserKartyArticleStep? step = null;
        var stepMode = false;
        if (request.Unit is { } sequence &&
            await ArticleStepProgress.UnitIdAsync(_context, sequence, cancellationToken) is { } unitId)
        {
            stepMode = true;
            step = await ArticleStepProgress.FindAsync(
                _context, request.UserId, unitId, cancellationToken);
            if (isCorrect)
            {
                step = ArticleStepProgress.CountCorrect(
                    _context, step, request.UserId, unitId, now);
            }
        }

        // Buraya yalnızca **kabul edilen** bir cevap ulaşıyor (geçersiz ve
        // mükerrer cevaplar yukarıda erken dönüyor). Doğru olması şart değil:
        // Artikel Pusulası'nda cevap vermek de günü "oynandı" yapar.
        await StreakActivityRecorder.MarkPlayedAsync(
            _context, request.UserId, now, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        return new AnswerArticlePracticeResponse
        {
            IsAccepted = true,
            IsCorrect = isCorrect,
            CorrectArticle = correctArticle,
            ArticleAttemptCount = learning.ArticleAttemptCount,
            ArticleCorrectCount = learning.ArticleCorrectCount,
            Step = stepMode ? ArticleStepProgress.ToResponse(step) : null,
        };
    }
}
