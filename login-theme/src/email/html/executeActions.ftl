<#-- Ссылка из админки: «задать пароль», «настроить 2FA»… — список действий, которые просит выполнить администратор. -->
<#outputformat "plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
</#outputformat>
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformExecuteActionsTitle")/>
<@layout.paragraph msg("platformExecuteActionsText", realmName, requiredActionsText)/>
<@layout.button link msg("platformExecuteActionsButton")/>
<@layout.note msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("platformExecuteActionsIgnore")/>
</@layout.emailLayout>
