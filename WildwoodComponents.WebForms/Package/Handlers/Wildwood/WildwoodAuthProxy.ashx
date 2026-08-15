<%--
    Same-origin proxy for the Wildwood Authentication control.

    The Class attribute points at a compiled type in WildwoodComponents.WebForms, so there
    is nothing here for you to build and nothing to keep in step with the package. Leave it
    as it is and the endpoints work; the control's ProxyBaseUrl already points here.

    Routes (all POST): /login, /register, /forgot-password, /two-factor-verify
--%>
<%@ WebHandler Language="C#" Class="WildwoodComponents.WebForms.Handlers.WildwoodAuthProxyHandler" %>
