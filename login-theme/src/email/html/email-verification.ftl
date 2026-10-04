<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title msg("platformEmailVerificationTitle")/>
<@layout.paragraph msg("platformEmailVerificationText", realmName)/>
<@layout.button link msg("platformEmailVerificationButton")/>
<@layout.note msg("platformEmailLinkExpires", linkExpirationFormatter(linkExpiration))/>
<@layout.note msg("platformEmailVerificationIgnore")/>
</@layout.emailLayout>
