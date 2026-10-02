<#ftl output_format="plainText">
${msg("sandboxPasswordResetTitle")}

${msg("sandboxPasswordResetText", realmName)}

${link}

${msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("sandboxPasswordResetIgnore")}
