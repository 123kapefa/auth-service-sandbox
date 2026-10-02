<#ftl output_format="plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
${msg("sandboxExecuteActionsTitle")}

${msg("sandboxExecuteActionsText", realmName, requiredActionsText)}

${link}

${msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("sandboxExecuteActionsIgnore")}
