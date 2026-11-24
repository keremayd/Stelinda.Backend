using Postgres.Models;

namespace Service.Helpers;

/// <summary>
/// Bilet ekonomisinin tek doğruluk kaynağı. Tavan ve yenilenme aralığı hem
/// API handler'ları hem de Hangfire yenileme işi tarafından buradan okunur;
/// iki yerde ayrı sabit tutulursa ekonomi sessizce tutarsızlaşır.
///
/// Bilet oyunun giriş bedelidir (her oyun 1 bilet; yanlış cevap bilet
/// yakmaz), biriktirilen para değil. Tavan, pasif yenilenmenin durduğu
/// noktadır; görev ödülü tavanı aşamaz.
///
/// 24 Kas 2026 (kullanıcı kararı): tavan 15 → 5, yenilenme 2 → 4 saat,
/// görev ödülleri günde 10 → 3. Ünite 3 deste olduğu için 15 bilet beş
/// üniteye yetiyordu, ücretsiz kullanıcı sınıra hiç takılmıyordu ve Premium
/// bir şey vaat etmiyordu. Duolingo'nun 5 canına yakın: bir oturumda 5 oyun,
/// sonrası bekle, görev yap ya da Premium. Değişirse Neon'daki
/// <c>create_user_lives()</c> fonksiyonu da elle güncellenmeli (migration'da yok).
/// </summary>
public static class LivesRules
{
    /// <summary>
    /// Yeni kullanıcının ve mevcut satırların bilet tavanı. Premium ileride
    /// bu değeri kullanıcı bazında yükseltecek, bu yüzden tavan kodda sabit
    /// kabul edilmez; satırdaki max_lives kolonu asıldır, bu sabit yalnızca
    /// yeni satırların başlangıç değeridir.
    /// </summary>
    public const int DefaultMaxLives = 5;

    /// <summary>Bir biletin pasif yenilenme süresi.</summary>
    public static readonly TimeSpan RegenInterval = TimeSpan.FromHours(4);

    /// <summary>
    /// Bilet harcandıktan sonra yenilenme zamanlayıcısını gerekiyorsa başlatır.
    ///
    /// Zamanlayıcı yalnızca tavandan ilk düşüşte kurulur: kullanıcı zaten
    /// tavanın altındaysa saat çoktan işliyordur ve her harcamada sıfırlansa
    /// çok oynayan oyuncu hiç bilet kazanamazdı.
    /// </summary>
    public static void StartRegenTimerIfNeeded(UserLives userLives, int livesBeforeSpend, DateTime utcNow)
    {
        if (livesBeforeSpend >= userLives.MaxLives)
        {
            userLives.LastLivesResetTime = utcNow.Add(RegenInterval);
        }
    }
}
