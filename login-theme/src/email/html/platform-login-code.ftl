<#-- Код входа из плагина code-login (EmailCodeSender): атрибуты code и ttlMinutes. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformCodeEmailTitle")/>
<@layout.paragraph msg("platformCodeEmailText", realmName)/>
<p style="margin:16px 0;padding:12px 16px;border:1px solid #d0d0d0;border-left:4px solid ${layout.brandColor};background:#f7f7f7;text-align:center;font-family:${layout.mono};font-size:28px;font-weight:600;letter-spacing:0.3em;color:#2e2e2e;">${code}</p>
<@layout.note msg("platformCodeEmailExpires", ttlMinutes)/>
<@layout.note msg("platformCodeEmailIgnore")/>
</@layout.emailLayout>
