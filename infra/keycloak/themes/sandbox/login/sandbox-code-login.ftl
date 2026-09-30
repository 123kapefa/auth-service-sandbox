<#-- Экран «Введите код» входа по коду (CodeLoginAuthenticator). Разметка — из макросов keycloak.v2, как login-otp.ftl. -->
<#import "template.ftl" as layout>
<#import "field.ftl" as field>
<#import "buttons.ftl" as buttons>
<@layout.registrationLayout displayMessage=true; section>
<!-- template: sandbox-code-login.ftl -->

    <#if section = "header">
        ${msg("sandboxCodeTitle")}
    <#elseif section = "form">
        <p id="sandbox-code-target">${msg("sandboxCodeSentTo", codeTarget)}</p>
        <form id="sandbox-code-form" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post" novalidate="novalidate">
            <@field.input name="code" label=msg("sandboxCodeLabel") autocomplete="one-time-code" autofocus=true />
            <@buttons.actionGroup>
                <@buttons.button id="kc-login" name="login" label="doLogIn" class=["kcButtonPrimaryClass", "kcButtonBlockClass"] />
            </@buttons.actionGroup>
            <@buttons.actionGroup>
                <@buttons.button id="sandbox-code-resend" name="resend" label="sandboxCodeResend" class=["kcButtonSecondaryClass", "kcButtonBlockClass"] />
            </@buttons.actionGroup>
        </form>
        <#if codeResendAfter != "0">
            <p id="sandbox-code-resend-after">${msg("sandboxCodeResendIn", codeResendAfter)}</p>
        </#if>
        <@buttons.actionGroup>
            <@buttons.buttonLink id="sandbox-code-back" href=url.loginRestartFlowUrl label="sandboxCodeBackToPassword" class=["kcButtonLinkClass"] />
        </@buttons.actionGroup>
    </#if>

</@layout.registrationLayout>
