using System.Net;
using AwsService.Abstractions;
using Common.Enums;
using Common.Exceptions;
using Common.Extensions;
using Stelinda.Api.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Service.DataTransferObjects.Requests;
using Service.DataTransferObjects.Requests.Karty;
using Service.DataTransferObjects.Requests.Profile;
using Service.DataTransferObjects.Responses;
using Service.DataTransferObjects.Responses.Profile;
using Service.Handlers.Commands;

namespace Stelinda.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAwsService _amazonService;

    public UserController(IMediator mediator, IAwsService amazonService)
    {
        _mediator = mediator;
        _amazonService = amazonService;
    }

    /// <summary>
    /// Bütün kullanıcıların kimlik ve takma adları. Uygulama kullanmıyor;
    /// herkese açıktı ve kimlikler başka uçlarda başkası adına istek atmaya
    /// yarıyordu. Yalnız yönetici (rol belirteçte henüz yok: kapalı).
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<GetAllUsersResponse>>>> GetAllUsers()
    {
        var response = await _mediator.Send(new GetAllUsersRequest());
        return Ok(new ApiResponse<List<GetAllUsersResponse>>()
        {
            Data = response
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<GetUserByIdResponse>>> GetUserById([FromRoute] string id)
    {
        var response = await _mediator.Send(new GetUserByIdRequest() { UserId = id });
        
        return Ok(new ApiResponse<GetUserByIdResponse>()
        {
            Data = response
        });
    }

    [HttpGet("{id}/learning-stats")]
    public async Task<ActionResult<ApiResponse<GetProfileLearningStatsResponse>>> GetProfileLearningStats(
        [FromRoute] string id)
    {
        var response = await _mediator.Send(new GetProfileLearningStatsRequest() { UserId = id });

        return Ok(new ApiResponse<GetProfileLearningStatsResponse>()
        {
            Data = response
        });
    }

    /// <summary>
    /// Ana dilin kaydı ("tr", "sq"). Uygulama dili değiştirdiğinde ve girişten
    /// sonra bir kez gönderir. Yalnız token sahibi.
    /// </summary>
    [Authorize]
    [HttpPut("{id}/language")]
    public async Task<ActionResult<ApiResponse<UpdateUserLanguageResponse>>> UpdateLanguage(
        [FromRoute] string id,
        [FromBody] UpdateUserLanguageBody body)
    {
        if (!string.Equals(User.Identity?.Name, id, StringComparison.Ordinal))
            return Forbid();

        var response = await _mediator.Send(new UpdateUserLanguageRequest
        {
            UserId = id,
            NativeLanguage = body.NativeLanguage,
        });

        return Ok(new ApiResponse<UpdateUserLanguageResponse> { Data = response });
    }

    [Authorize]
    [HttpGet("{id}/word-collection")]
    public async Task<ActionResult<ApiResponse<GetWordCollectionResponse>>> GetWordCollection(
        [FromRoute] string id,
        [FromQuery] string category = "all",
        [FromQuery] int page = 1)
    {
        if (!string.Equals(User.Identity?.Name, id, StringComparison.Ordinal))
            return Forbid();

        var response = await _mediator.Send(new GetWordCollectionRequest
        {
            UserId = id,
            Category = category,
            Page = page,
            Locale = Request.NativeLanguage(),
        });

        foreach (var item in response.Items)
        {
            if (!string.IsNullOrEmpty(item.KartyUrl))
                item.KartyUrl = _amazonService.GeneratePreSignedUrl(
                    item.KartyUrl, BucketType.KartyAssets);
            if (!string.IsNullOrEmpty(item.AudioUrl))
                item.AudioUrl = _amazonService.GeneratePreSignedUrl(
                    item.AudioUrl, BucketType.KartyAssets);
        }

        return Ok(new ApiResponse<GetWordCollectionResponse> { Data = response });
    }

    /// <summary>
    /// Hesabı ve bütün verisini kalıcı olarak siler. Yalnız hesabın sahibi
    /// silebilir: yoldaki kimlik token'daki kullanıcıyla aynı olmalı.
    /// Apple ile bağlı hesapta gövde Apple'ın yetki kodunu taşır; yoksa
    /// E4130 döner (bkz. <see cref="DeleteAccountCommandHandler"/>). Gövde
    /// isteğe bağlı: eski istemci göndermez.
    /// </summary>
    [Authorize]
    [HttpDelete("{id}/account")]
    public async Task<ActionResult<ApiResponse<DeleteAccountResponse>>> DeleteAccount(
        [FromRoute] string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteAccountBody? body)
    {
        if (!string.Equals(User.Identity?.Name, id, StringComparison.Ordinal))
            return Forbid();

        var response = await _mediator.Send(new DeleteAccountRequest
        {
            UserId = id,
            AppleAuthorizationCode = body?.AppleAuthorizationCode,
            SkipAppleRevocation = body?.SkipAppleRevocation ?? false,
        });
        return Ok(new ApiResponse<DeleteAccountResponse> { Data = response });
    }

    [HttpPost("{id}/daily-activity")]
    public async Task<ActionResult<ApiResponse<RecordDailyActivityResponse>>> RecordDailyActivity(
        [FromRoute] string id)
    {
        var response = await _mediator.Send(new RecordDailyActivityRequest() { UserId = id });

        return Ok(new ApiResponse<RecordDailyActivityResponse>()
        {
            Data = response
        });
    }
    
    [HttpPost("{id}/streak/resolve-freeze")]
    public async Task<ActionResult<ApiResponse<RecordDailyActivityResponse>>> ResolveStreakFreeze(
        [FromRoute] string id,
        [FromQuery] bool useFreeze)
    {
        var response = await _mediator.Send(new ResolveStreakFreezeRequest
        {
            UserId = id,
            UseFreeze = useFreeze
        });

        return Ok(new ApiResponse<RecordDailyActivityResponse>()
        {
            Data = response
        });
    }

    [HttpPost("update-score")]
    public async Task<ActionResult<ApiResponse<UpdateScoreResponse>>> UpdateScoreById([FromBody] UpdateScoreRequest request)
    {
        var updateScoreResponse = await _mediator.Send(new UpdateScoreRequest()
        {
            UserId = request.UserId,
            Point = request.Point,
            BoostActive = request.BoostActive,
            KartyId = request.KartyId
        });

        // Yanlış cevap Rövanş'a düşer ve kutuyu sıfırlar, bilet yakmaz: bedel
        // yalnız oyuna girişte alınır.
        if (request.Point == -1 && request.KartyId.HasValue)
        {
            await _mediator.Send(new RecordWrongKartyRequest()
            {
                UserId = request.UserId,
                KartyId = request.KartyId.Value
            });
        }

        return Ok(new ApiResponse<UpdateScoreResponse>()
        {
            Data = new UpdateScoreResponse()
            {
                UserId = updateScoreResponse.UserId,
                Score = updateScoreResponse.Score,
                Experience = updateScoreResponse.Experience,
            }
        });
    }
    
    [HttpPost("update-lives")]
    public async Task<ActionResult<ApiResponse<UpdateLivesResponse>>> UpdateLivesById([FromBody] UpdateLivesRequest request)
    {
        var updateLivesResponse = await _mediator.Send(new UpdateLivesRequest() { UserId = request.UserId });

        // Bu uç oyuna giriş bedelidir: bilet düşülemediyse oyun açılmamalı.
        // VideoController.GetRandomQuestionByUserId ile aynı kalıp.
        if (!updateLivesResponse.Spent)
        {
            throw new StelindaException(ErrorCode.TheUserHasNoLives.CreateMessage(updateLivesResponse.Lives),
                ErrorCode.TheUserHasNoLives.GetDescription(updateLivesResponse.Lives), HttpStatusCode.BadRequest);
        }

        return Ok(new ApiResponse<UpdateLivesResponse>()
        {
            Data = updateLivesResponse
        });
    }
    
    [HttpGet("{id}/score-with-lives")]
    public async Task<ActionResult<ApiResponse<GetScoreWithLivesByIdResponse>>> GetScoreWithLivesById([FromRoute] string id)
    {
        var getScoreByIdResponse = await _mediator.Send(new GetScoreByIdRequest() { UserId = id });
        
        var getLivesByIdResponse = await _mediator.Send(new GetLivesByIdRequest() { UserId = id });

        var getLeagueByUserIdResponse = await _mediator.Send(new GetLeagueByUserIdRequest() { UserId = id, Locale = Request.NativeLanguage() });

        var getStreakResponse = await _mediator.Send(new GetStreakByIdRequest() { UserId = id });

        return Ok(new ApiResponse<GetScoreWithLivesByIdResponse>()
        {
            Data = new GetScoreWithLivesByIdResponse()
            {
                UserId = id,
                UserNickname = getScoreByIdResponse.UserNickname,
                Score = getScoreByIdResponse.Score,
                Experience = getScoreByIdResponse.Experience,
                Lives = getLivesByIdResponse.Lives,
                MaxLives = getLivesByIdResponse.MaxLives,
                // Tavandayken beklenen bir bilet yok; zamanlayıcı ancak
                // tavandan ilk düşüşte kurulduğu için o durumda anlamsız.
                NextTicketAt = getLivesByIdResponse.Lives < getLivesByIdResponse.MaxLives
                    ? getLivesByIdResponse.LastLivesResetTime
                    : null,
                League = getLeagueByUserIdResponse,
                Streak = getStreakResponse.CurrentStreak,
                LongestStreak = getStreakResponse.LongestStreak,
                FreezeCount = getStreakResponse.FreezeCount,
                PlayedToday = getStreakResponse.PlayedToday,
                StreakWeek = getStreakResponse.Week,
                IsPremium = getLivesByIdResponse.IsPremium,
                PremiumUntil = getLivesByIdResponse.PremiumUntil,
            }
        });
    }

    [HttpGet("{id}/league")]
    public async Task<ActionResult<ApiResponse<LeagueProgressResponse>>> GetLeagueByUserId([FromRoute] string id)
    {
        var response = await _mediator.Send(new GetLeagueByUserIdRequest() { UserId = id, Locale = Request.NativeLanguage() });

        return Ok(new ApiResponse<LeagueProgressResponse>()
        {
            Data = response
        });
    }

    [HttpPost("{id}/league/promotion/acknowledge")]
    public async Task<ActionResult<ApiResponse<AcknowledgeLeaguePromotionResponse>>>
        AcknowledgeLeaguePromotion([FromRoute] string id)
    {
        var response = await _mediator.Send(new AcknowledgeLeaguePromotionRequest
        {
            UserId = id
        });

        return Ok(new ApiResponse<AcknowledgeLeaguePromotionResponse>
        {
            Data = response
        });
    }
    
    /// <summary>Profil fotoğrafının üst sınırı; uygulama yüklemeden önce küçültür.</summary>
    private const long MaxProfilePhotoBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Profil fotoğrafı yalnız hesabın sahibince yüklenir (OwnUserFilter).
    /// Türü istemcinin söylediğinden ve dosya adından değil dosyanın ilk
    /// baytlarından anlaşılır; yalnız JPEG, PNG, HEIC ve WebP, en çok 5 MB.
    /// </summary>
    [HttpPost("{id}/upload-profile-photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxProfilePhotoBytes + 64 * 1024)]
    public async Task<ActionResult<ApiResponse<UploadUserFileResponse>>> UploadUserFile([FromRoute] string id, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse<string> { Message = "Dosya boş." });
        if (file.Length > MaxProfilePhotoBytes)
            return BadRequest(new ApiResponse<string> { Message = "Fotoğraf en çok 5 MB olabilir." });

        var image = await SniffImageAsync(file);
        if (image is null)
            return BadRequest(new ApiResponse<string> { Message = "Yalnız JPEG, PNG, HEIC ya da WebP fotoğraf yüklenebilir." });

        var user = await _mediator.Send(new GetUserByIdRequest() { UserId = id });

        var key = $"profile-photos/{id}/profile{image.Value.Extension}";

        await using (var stream = file.OpenReadStream())
        {
            await _amazonService.UploadFileAsync(key, stream, image.Value.ContentType);
        }

        await _mediator.Send(new UploadPhotoByIdRequest() { Id = user.UserId, PhotoUrl = key });

        var photoSignedUrl = _amazonService.GeneratePreSignedUrl(key, BucketType.ProfilePhotos);
        
        return Ok(new ApiResponse<UploadUserFileResponse> 
        { 
            Data = new UploadUserFileResponse()
            {
                Id = user.UserId,
                ProfilePhoto = key,
                SignedUrl = photoSignedUrl
            } 
        });
    }

    /// <summary>Dosyanın ilk baytlarından resim türü; tanınmazsa null.</summary>
    private static async Task<(string Extension, string ContentType)?> SniffImageAsync(IFormFile file)
    {
        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(head.AsMemory(0, head.Length));
        if (read < 12) return null;

        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return (".jpg", "image/jpeg");
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
            return (".png", "image/png");
        var ascii = System.Text.Encoding.ASCII;
        if (ascii.GetString(head, 0, 4) == "RIFF" && ascii.GetString(head, 8, 4) == "WEBP")
            return (".webp", "image/webp");
        if (ascii.GetString(head, 4, 4) == "ftyp")
        {
            var brand = ascii.GetString(head, 8, 4);
            if (brand is "heic" or "heix" or "hevc" or "mif1" or "msf1")
                return (".heic", "image/heic");
        }
        return null;
    }
}

/// <summary>Ana dil gövdesi: <c>{"nativeLanguage": "sq"}</c>.</summary>
public class UpdateUserLanguageBody
{
    public string NativeLanguage { get; set; } = string.Empty;
}
