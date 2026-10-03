<#-- Каркас писем: название проекта, белая карточка, подпись. Стили — в атрибутах: почтовые программы не читают <style>. -->
<#macro emailLayout>
<!doctype html>
<html lang="${locale.language}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light">
</head>
<body style="margin:0;padding:0;background:#f9fafb;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f9fafb;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#111826;">
  <tr>
    <td align="center" style="padding:32px 16px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;">
        <tr>
          <td style="padding:0 0 16px;text-align:center;font-size:12px;font-weight:600;letter-spacing:0.08em;text-transform:uppercase;color:#677383;">${realmName!""}</td>
        </tr>
        <tr>
          <td style="background:#ffffff;border:1px solid #e0e5eb;border-radius:12px;padding:28px;font-size:14px;line-height:1.6;">
            <#nested>
          </td>
        </tr>
        <tr>
          <td style="padding:16px 0 0;text-align:center;font-size:12px;color:#677383;">${msg("platformEmailFooter")}</td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
</#macro>

<#macro title text>
<h1 style="margin:0 0 12px;font-size:18px;font-weight:600;line-height:1.3;color:#111826;">${text}</h1>
</#macro>

<#macro paragraph text>
<p style="margin:0 0 12px;">${text}</p>
</#macro>

<#macro note text>
<p style="margin:12px 0 0;font-size:13px;color:#677383;">${text}</p>
</#macro>

<#-- Кнопка-ссылка и та же ссылка текстом: кнопку рисуют не все почтовые программы. -->
<#macro button href label>
<table role="presentation" cellpadding="0" cellspacing="0" style="margin:20px 0;">
  <tr>
    <td style="border-radius:8px;background:#1e49b7;">
      <a href="${href}" target="_blank" style="display:inline-block;padding:10px 20px;font-size:14px;font-weight:600;color:#ffffff;text-decoration:none;">${label}</a>
    </td>
  </tr>
</table>
<p style="margin:0 0 12px;font-size:12px;color:#677383;">${msg("platformEmailLinkFallback")}<br><a href="${href}" target="_blank" style="color:#1e49b7;word-break:break-all;">${href}</a></p>
</#macro>
