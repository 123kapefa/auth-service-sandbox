<#ftl output_format="plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
${msg("platformExecuteActionsTitle")}

${msg("platformExecuteActionsText", realmName, requiredActionsText)}

${link}

${msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("platformExecuteActionsIgnore")}
