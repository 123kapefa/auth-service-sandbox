<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("sandboxEmailVerificationTitle")/>
<@layout.paragraph msg("sandboxEmailVerificationText", realmName)/>
<@layout.button link msg("sandboxEmailVerificationButton")/>
<@layout.note msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("sandboxEmailVerificationIgnore")/>
</@layout.emailLayout>
