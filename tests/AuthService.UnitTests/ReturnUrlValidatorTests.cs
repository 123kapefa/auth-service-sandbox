using AuthService.Core.Features.Auth.ReturnUrls;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthService.UnitTests;

public sealed class ReturnUrlValidatorTests
{
    private readonly ReturnUrlValidator _validator = new(Options.Create(new ReturnUrlOptions
    {
        AllowedOrigins = ["https://shop.example.com", "http://localhost:3000"],
        Default = "/",
    }));

    [Theory]
    [InlineData("/")]
    [InlineData("/me/")]
    [InlineData("/orders?id=1#top")]
    [InlineData("https://shop.example.com/cart")]
    [InlineData("https://SHOP.example.com/")]           // регистр хоста не важен
    [InlineData("http://localhost:3000/after-login")]
    public void Allowed_ReturnedAsIs(string returnTo) =>
        Assert.Equal(returnTo, _validator.Resolve(returnTo));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/phish")]            // чужой домен
    [InlineData("https://shop.example.com.evil.example/")] // похожий домен
    [InlineData("//evil.example/")]                       // браузер считает это другим хостом
    [InlineData("/\\evil.example/")]                      // то же через обратный слэш
    [InlineData("https://shop.example.com@evil.example/")] // user@host — хост на самом деле evil
    [InlineData("https://user:pass@shop.example.com/")]   // учётные данные в адресе — не принимаем даже с нашим хостом
    [InlineData("http://shop.example.com/")]              // другая схема — другой origin
    [InlineData("https://shop.example.com:8443/")]        // другой порт — другой origin
    [InlineData("javascript:alert(1)")]
    [InlineData("shop.example.com/cart")]                 // без схемы и не с "/"
    public void NotAllowed_ReplacedWithDefault(string? returnTo) =>
        Assert.Equal("/", _validator.Resolve(returnTo));
}
