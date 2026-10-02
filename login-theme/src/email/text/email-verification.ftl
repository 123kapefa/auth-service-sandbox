<#ftl output_format="plainText">
${msg("sandboxEmailVerificationTitle")}

${msg("sandboxEmailVerificationText", realmName)}

${link}

${msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("sandboxEmailVerificationIgnore")}
