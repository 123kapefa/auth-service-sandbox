<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformPasswordResetTitle")/>
<@layout.paragraph msg("platformPasswordResetText", realmName)/>
<@layout.button link msg("platformPasswordResetButton")/>
<@layout.note msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("platformPasswordResetIgnore")/>
</@layout.emailLayout>
