<#ftl output_format="plainText">
${msg("platformEmailVerificationTitle")}

${msg("platformEmailVerificationText", realmName)}

${link}

${msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("platformEmailVerificationIgnore")}
