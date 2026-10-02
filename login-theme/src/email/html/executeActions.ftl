<#-- Ссылка из админки: «задать пароль», «настроить 2FA»… — список действий, которые просит выполнить администратор. -->
<#outputformat "plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
</#outputformat>
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("sandboxExecuteActionsTitle")/>
<@layout.paragraph msg("sandboxExecuteActionsText", realmName, requiredActionsText)/>
<@layout.button link msg("sandboxExecuteActionsButton")/>
<@layout.note msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("sandboxExecuteActionsIgnore")/>
</@layout.emailLayout>
