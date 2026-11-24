namespace Service.DataTransferObjects.Responses.ArticlePractice;

public class AnswerArticlePracticeResponse
{
    public bool IsAccepted { get; set; }
    public bool IsCorrect { get; set; }
    public string CorrectArticle { get; set; } = string.Empty;
    public int ArticleAttemptCount { get; set; }
    public int ArticleCorrectCount { get; set; }

    /// <summary>İstekte ünite varsa Artikel adımının cevaptan sonraki ilerlemesi.</summary>
    public ArticleStepProgressResponse? Step { get; set; }
}
