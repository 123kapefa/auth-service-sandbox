<#-- Каркас писем в сером квадратном стиле: логотип и название проекта (или приложения), карточка с квадратом цвета
     проекта в углу, подпись. branding — от плагина (BrandingEmailTemplateProvider): name, color, onColor, logoUrl;
     без него — серый стиль и название realm. Стили — в атрибутах: почтовые программы не читают <style>. -->
<#assign brandName = (branding.name)!(realmName!"")>
<#assign brandColor = (branding.color)!"#3c3c3c">
<#assign brandOnColor = (branding.onColor)!"#f7f7f7">
<#assign mono = "'JetBrains Mono',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace">
<#macro emailLayout>
<!doctype html>
<html lang="${locale.language}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light">
</head>
<body style="margin:0;padding:0;background:#f7f7f7;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f7f7;font-family:'Golos Text',system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#2e2e2e;">
  <tr>
    <td align="center" style="padding:32px 16px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;">
        <tr>
          <td style="padding:0 0 16px;text-align:center;">
            <#if (branding.logoUrl)??><img src="${branding.logoUrl}" alt="" height="28" style="height:28px;max-width:160px;vertical-align:middle;border:0;margin-right:8px;"><#else><span style="display:inline-block;width:10px;height:10px;background:${brandColor};vertical-align:middle;margin-right:8px;"></span></#if><span style="vertical-align:middle;font-family:${mono};font-size:11px;font-weight:500;letter-spacing:0.16em;text-transform:uppercase;color:#6a6a6a;">${brandName}</span>
          </td>
        </tr>
        <tr>
          <td style="background:#eeeeee;border:1px solid #d0d0d0;border-top:4px solid ${brandColor};padding:28px;font-size:14px;line-height:1.6;">
            <#nested>
          </td>
        </tr>
        <tr>
          <td style="padding:16px 0 0;text-align:center;font-size:12px;color:#6a6a6a;">${msg("platformEmailFooter")}</td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
</#macro>

<#macro title text>
<h1 style="margin:0 0 12px;font-size:18px;font-weight:600;line-height:1.3;color:#2e2e2e;">${text}</h1>
</#macro>

<#macro paragraph text>
<p style="margin:0 0 12px;">${text}</p>
</#macro>

<#macro note text>
<p style="margin:12px 0 0;font-size:13px;color:#6a6a6a;">${text}</p>
</#macro>

<#-- Кнопка-ссылка и та же ссылка текстом: кнопку рисуют не все почтовые программы. -->
<#macro button href label>
<table role="presentation" cellpadding="0" cellspacing="0" style="margin:20px 0;">
  <tr>
    <td style="background:${brandColor};box-shadow:4px 4px 0 #c4c4c4;">
      <a href="${href}" target="_blank" style="display:inline-block;padding:10px 20px;font-size:14px;font-weight:600;color:${brandOnColor};text-decoration:none;">${label}</a>
    </td>
  </tr>
</table>
<p style="margin:0 0 12px;font-size:12px;color:#6a6a6a;">${msg("platformEmailLinkFallback")}<br><a href="${href}" target="_blank" style="color:#2e2e2e;word-break:break-all;">${href}</a></p>
</#macro>
