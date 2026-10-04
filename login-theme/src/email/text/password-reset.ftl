<#ftl output_format="plainText">
${msg("platformPasswordResetTitle")}

${msg("platformPasswordResetText", realmName)}

${link}

${msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("platformPasswordResetIgnore")}
