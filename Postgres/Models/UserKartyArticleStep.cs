using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Postgres.Models;

/// <summary>
/// Kullanıcının bir ünitedeki Artikel adımı (24 Kas 2026): turda doğru
/// bilinen artikel sayısı ve adımın bittiği an. Tur yarıda bırakılırsa sayı
/// kalır, adım kaldığı yerden sürer (kullanıcı kararı).
/// </summary>
[Table("user_karty_article_step")]
[PrimaryKey(nameof(UserId), nameof(KartyUnitId))]
public class UserKartyArticleStep
{
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("karty_unit_id")]
    public int KartyUnitId { get; set; }

    /// <summary>Bu adımda doğru bilinen artikel; hedefe (<c>ArticleStepRules.RoundGoal</c>) varınca adım biter.</summary>
    [Column("correct_count")]
    public int CorrectCount { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
