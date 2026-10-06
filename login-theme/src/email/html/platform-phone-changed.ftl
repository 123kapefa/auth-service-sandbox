<#-- Уведомление о смене номера из плагина code-login (UpdatePhoneAction): атрибуты noticeText и noticeNotYou. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformPhoneChangedEmailTitle")/>
<@layout.paragraph noticeText/>
<@layout.note noticeNotYou/>
</@layout.emailLayout>
