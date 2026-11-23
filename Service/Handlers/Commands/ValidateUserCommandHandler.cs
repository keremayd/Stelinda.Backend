using System.Net;
using Common.Enums;
using Common.Exceptions;
using Common.Extensions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Postgres;
using Postgres.Abstractions;
using Postgres.Models;
using Service.DataTransferObjects.Requests;
using Service.DataTransferObjects.Responses;

namespace Service.Handlers.Commands;

public class ValidateUserCommandHandler : IRequestHandler<ValidateUserRequest, ValidateUserResponse>
{
    private readonly IStelindaGenericRepository<User> _genericRepository;
    private readonly StelindaContext _context;
    private readonly UserManager<User> _userManager;

    public ValidateUserCommandHandler(IStelindaGenericRepository<User> genericRepository, UserManager<User> userManager)
    {
        _genericRepository = genericRepository;
        _userManager = userManager;
        _context = genericRepository.GetDbContext();
    }

    public async Task<ValidateUserResponse> Handle(ValidateUserRequest request, CancellationToken cancellationToken)
    {
        // Kayıtsız e-posta ile yanlış şifre aynı cevabı alır: ayrı hata kodu
        // (ya da kısa süren cevap) bir e-postanın kayıtlı olup olmadığını
        // söylüyordu. Kayıtsız e-postada da bir şifre özeti doğrulanır ki
        // süre farkı da kalmasın.
        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await _userManager.FindByEmailAsync(request.Email);
        var passwordOk = user is { PasswordHash: not null }
            ? await _userManager.CheckPasswordAsync(user, request.Password ?? string.Empty)
            : VerifyAgainstDummy(request.Password);
        if (user == null || !passwordOk)
            throw new StelindaException(ErrorCode.TheUserAuthenticatedFailed.CreateMessage(),
                ErrorCode.TheUserAuthenticatedFailed.GetDescription(), HttpStatusCode.Unauthorized);
        
        return new ValidateUserResponse()
        {
            User = user
        };
    }

    /// <summary>Kayıtsız e-postada doğrulanan özet; uygulamanın kendi şifre
    /// özetleyicisiyle bir kez üretilir.</summary>
    private static string? _dummyHash;

    /// <summary>Yalnız süreyi eşitler: sonuç ne olursa olsun false döner, yani
    /// şifresi olmayan (Google/Apple) hesaba bu yoldan girilemez.</summary>
    private bool VerifyAgainstDummy(string? password)
    {
        var hasher = _userManager.PasswordHasher;
        _dummyHash ??= hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(new User(), _dummyHash, password ?? string.Empty);
        return false;
    }
}