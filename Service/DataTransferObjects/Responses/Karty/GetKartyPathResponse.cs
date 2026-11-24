namespace Service.DataTransferObjects.Responses.Karty;

/// <summary>
/// Karty Yolu: üniteler ve her ünitenin adımları. Bir adım bir deste:
/// ünitenin kelimeleri ünite sırasıyla 8'erli bölünür (<c>KartyDeckRules</c>).
/// Görsel alanları S3 anahtarıdır, controller imzalı adrese çevirir.
/// </summary>
public class GetKartyPathResponse
{
    public List<KartyPathUnitResponse> Units { get; set; } = new();

    /// <summary>Bir destedeki kelime sayısı (şu anki destenin halkasındaki parça sayısı).</summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Açılıp kutlaması henüz gösterilmemiş ünitenin sırası; yoksa null.
    /// İstemci patikaya dönünce kutlamayı gösterir ve
    /// <c>path/{userId}/unlock/acknowledge</c> ile kapatır.
    /// </summary>
    public int? PendingUnlockSequence { get; set; }
}

public class KartyPathUnitResponse
{
    public int Sequence { get; set; }

    /// <summary>Kullanıcının ana dilinde; o dilde yoksa varsayılan dilde, o da yoksa ilk bulunan dilde (<c>LocaleRules.Default</c>).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary><c>done</c> · <c>current</c> · <c>open</c> · <c>locked</c>.</summary>
    public string State { get; set; } = string.Empty;

    public int WordCount { get; set; }

    /// <summary><c>box ≥ 2</c> olan kelime sayısı.</summary>
    public int ConsolidatedCount { get; set; }

    public double UnlockGoal { get; set; }
    public string? EmblemUrl { get; set; }
    public List<KartyPathStepResponse> Steps { get; set; } = new();
}

public class KartyPathStepResponse
{
    /// <summary><c>done</c> · <c>current</c> · <c>upcoming</c> · <c>locked</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Ünite içindeki deste numarası (1'den). Açık bütün desteler bittiğinde
    /// eklenen yalnız tekrar adımında null.
    /// </summary>
    public int? Deck { get; set; }

    /// <summary>Bu destede tanışılan kelime.</summary>
    public int NewCount { get; set; }

    /// <summary>
    /// Bu destede en az bir kez doğru bilinen kelime (kutu ≥ 1). Deste bu
    /// sayı destenin büyüklüğüne varınca biter (<c>KartyDeckRules.IsComplete</c>);
    /// istemcinin kelime halkası ve "3/8 yeni" bunu gösterir, tanışılanı
    /// değil: tanışılıp yanlış bilinen kelimeyle halka dolup deste
    /// açılmıyordu (19 Kas 2026, kullanıcı).
    /// </summary>
    public int LearnedCount { get; set; }

    /// <summary>Destenin büyüklüğü.</summary>
    public int NewGoal { get; set; }

    /// <summary>Şu anki adımda: bütün ünitelerden vadesi gelmiş tekrar.</summary>
    public int ReviewCount { get; set; }

    /// <summary>Bu destede tekrar vakti gelmiş kelime (bitmiş düğümün rozeti).</summary>
    public int DueCount { get; set; }

    /// <summary>Destenin tanışılmış kelimeleri, deste sırasıyla.</summary>
    public List<KartyPathWordResponse> Words { get; set; } = new();

    /// <summary>Destenin henüz tanışılmamış kelimelerinin görselleri; istemci silüet çizer.</summary>
    public List<string> UpcomingImageUrls { get; set; } = new();

    /// <summary>
    /// Ünitenin pekiştirme adımı (24 Kas 2026): desteler bitti, ünite hedefe
    /// ulaşmadı. İstemci ayrı bir düğüm çizmez, ünitenin pekiştirme kapısı şu
    /// anki düğüm olur; oyunu <c>karty/random?reinforce=true</c> ile açar.
    /// <see cref="Deck"/> null.
    /// </summary>
    public bool Reinforce { get; set; }

    /// <summary>
    /// Ünitenin Artikel adımı (24 Kas 2026, <c>ArticleStepRules</c>): ünitenin
    /// ortasında kendi düğümü. <see cref="LearnedCount"/> turda doğru bilinen
    /// artikel, <see cref="NewGoal"/> turun hedefi; oyunu
    /// <c>article-practice/next/{userId}?unit=</c> ile açar. <see cref="Deck"/> null.
    /// </summary>
    public bool Article { get; set; }
}

public class KartyPathWordResponse
{
    public int KartyId { get; set; }
    public string NounText { get; set; } = string.Empty;
    public string Article { get; set; } = string.Empty;
    public string KartyUrl { get; set; } = string.Empty;

    /// <summary>
    /// Kelimenin kullanıcının ana dilindeki anlamı (<c>karty_translation</c>);
    /// o dilde girilmemişse <c>null</c>.
    /// </summary>
    public string? Meaning { get; set; }
    public int Box { get; set; }
    public bool IsDue { get; set; }

    /// <summary>Kelime pekişti mi (ünitenin pekiştirme oyununda doğru bilindi).</summary>
    public bool Reinforced { get; set; }
}
