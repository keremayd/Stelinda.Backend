using MediatR;
using Service.DataTransferObjects.Responses.ArticlePractice;

namespace Service.DataTransferObjects.Requests.ArticlePractice;

public class GetArticlePracticeTaskRequest : IRequest<GetArticlePracticeTaskResponse>
{
    public string UserId { get; set; } = string.Empty;
    public int? PreferredKartyId { get; set; }

    /// <summary>
    /// Patikanın Artikel adımı oynanıyorsa ünitenin sırası (24 Kas 2026):
    /// kelimeler artikel takvimine göre seçilir, yanıtta adımın ilerlemesi
    /// döner. Boşsa eski Artikel Pusulası (yan durak) davranışı.
    /// </summary>
    public int? Unit { get; set; }
}
