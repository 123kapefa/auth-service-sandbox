<#-- Уведомление о способах входа из плагина code-login (AccountNoticeResource): атрибуты noticeText и noticeNotYou. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformSignInMethodsEmailTitle")/>
<@layout.paragraph noticeText/>
<@layout.note noticeNotYou/>
</@layout.emailLayout>
