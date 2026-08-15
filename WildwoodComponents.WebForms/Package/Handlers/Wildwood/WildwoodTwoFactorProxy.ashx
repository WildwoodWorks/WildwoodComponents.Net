<%--
    Same-origin proxy for the Wildwood Two-Factor Settings control.

    The Class attribute points at a compiled type in WildwoodComponents.WebForms, so there
    is nothing here for you to build. Every route requires a signed-in user.

    Routes: POST /email/enroll, POST /email/verify, POST /authenticator/enroll,
            POST /authenticator/verify, POST /credentials/{id}/set-primary,
            POST /recovery-codes/regenerate, DELETE /credentials/{id},
            DELETE /trusted-devices/{id}, DELETE /trusted-devices
--%>
<%@ WebHandler Language="C#" Class="WildwoodComponents.WebForms.Handlers.WildwoodTwoFactorProxyHandler" %>
