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
    [InlineData("   ")]                                   // только пробелы
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

    // Браузер выбрасывает TAB, LF и CR из адреса: "/<TAB>/evil.example" он читает как "//evil.example" — чужой хост.
    // Сервер видит значение уже после декодирования (%09 → TAB), поэтому управляющие и невидимые символы не принимаются нигде.
    [Theory]
    [InlineData("/\t/evil.example")]                        // TAB между слэшами
    [InlineData("/\n/evil.example")]                        // LF
    [InlineData("/\r/evil.example")]                        // CR (раньше — 500: заголовок Location с CR)
    [InlineData("/\r\n/evil.example")]
    [InlineData("/\t\\evil.example")]                      // TAB и обратный слэш
    [InlineData("/ /evil.example")]                         // пробел
    [InlineData("/\u3000/evil.example")]                    // юникод-пробел
    [InlineData("/\u00A0/evil.example")]                    // неразрывный пробел
    [InlineData("/\u2028/evil.example")]                    // разделитель строк
    [InlineData("/\u200B/evil.example")]                    // нулевая ширина (категория Format)
    [InlineData("/\u0000/evil.example")]                    // NUL
    [InlineData("/\u007F/evil.example")]                    // DEL
    [InlineData("/\U000E0001/evil.example")]               // символ-тег вне базовой плоскости (категория Format)
    [InlineData("/orders\t")]                              // и в конце обычного пути
    [InlineData("/orders?next=\n/evil.example")]            // и в параметрах
    [InlineData("https://shop.example.com/\t/evil")]        // и в абсолютном адресе из белого списка
    [InlineData("https://shop.example.com/cart\r\nSet-Cookie: x=1")]
    public void ControlOrInvisibleCharacters_ReplacedWithDefault(string returnTo) =>
        Assert.Equal("/", _validator.Resolve(returnTo));

    [Theory]
    [InlineData("/orders?id=1#top", "/orders?id=1#top")]
    [InlineData("/заказы/1", "/%D0%B7%D0%B0%D0%BA%D0%B0%D0%B7%D1%8B/1")]
    [InlineData("/caf\u00E9?q=ü", "/caf%C3%A9?q=%C3%BC")]
    [InlineData("https://shop.example.com/cart", "https://shop.example.com/cart")]
    public void ToLocation_NonAsciiEncodedForHeader(string returnTo, string expected) =>
        Assert.Equal(expected, ReturnUrlValidator.ToLocation(returnTo));

    [Fact]
    public void ToLocation_LoneSurrogate_BecomesReplacementCharacter_NoException()
    {
        // Строка собирается в коде: xUnit портит одинокий суррогат в InlineData.
        string withLoneSurrogate = "/aA" + (char)0xD800 + "b";

        Assert.Equal("/aA%EF%BF%BDb", ReturnUrlValidator.ToLocation(withLoneSurrogate));
    }

    [Theory]
    [InlineData("/caf\u00E9")]                              // обычные не-ASCII буквы допустимы
    [InlineData("/заказы/1")]
    public void NonAsciiLetters_StillAllowed(string returnTo) =>
        Assert.Equal(returnTo, _validator.Resolve(returnTo));
}
