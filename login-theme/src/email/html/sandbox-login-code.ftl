<#-- Код входа из плагина code-login (EmailCodeSender): атрибуты code и ttlMinutes. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("sandboxCodeEmailTitle")/>
<@layout.paragraph msg("sandboxCodeEmailText", realmName)/>
<p style="margin:16px 0;padding:12px 16px;border-radius:8px;background:#eff2f6;text-align:center;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:28px;font-weight:600;letter-spacing:0.3em;color:#111826;">${code}</p>
<@layout.note msg("sandboxCodeEmailExpires", ttlMinutes)/>
<@layout.note msg("sandboxCodeEmailIgnore")/>
</@layout.emailLayout>
