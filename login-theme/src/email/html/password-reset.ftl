<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("sandboxPasswordResetTitle")/>
<@layout.paragraph msg("sandboxPasswordResetText", realmName)/>
<@layout.button link msg("sandboxPasswordResetButton")/>
<@layout.note msg("sandboxEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("sandboxPasswordResetIgnore")/>
</@layout.emailLayout>
