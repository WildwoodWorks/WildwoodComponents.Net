WildwoodComponents.WebForms
===========================

Wildwood platform components for classic ASP.NET WebForms sites (.NET Framework 4.8).

WHAT THE INSTALL JUST ADDED
---------------------------
  Controls\Wildwood\*.ascx        the components you drop onto a page
  Handlers\Wildwood\*.ashx        working proxy endpoints (no code to write)
  Scripts\wildwood\*.js           component behaviour
  Content\wildwood\*.css          component styling
  web.config                      Wildwood appSettings + the token module

1. FINISH THE CONFIGURATION
---------------------------
Open web.config and fill in the two blank values under <appSettings>:

  <add key="WildwoodAPI:ApiKey" value="YOUR-APP-API-KEY" />
  <add key="WildwoodAPI:AppId"  value="YOUR-APP-ID" />

Both come from your app's Details page in WildwoodAdmin
(https://admin.wildwoodworks.io).

That is all that is required. Configuration loads on first use. To fail fast at
startup instead, or to configure from somewhere other than web.config, call this
from Global.asax:

  void Application_Start(object sender, EventArgs e)
  {
      WildwoodComponents.WebForms.WildwoodWebForms.Configure();
  }

2. ADD A COMPONENT TO A PAGE
----------------------------
  <%@ Register TagPrefix="ww" TagName="Authentication"
               Src="~/Controls/Wildwood/AuthenticationControl.ascx" %>

  <ww:Authentication runat="server" ReturnUrl="~/Default.aspx" />

  <%@ Register TagPrefix="ww" TagName="TwoFactorSettings"
               Src="~/Controls/Wildwood/TwoFactorSettingsControl.ascx" %>

  <ww:TwoFactorSettings runat="server" />

The controls emit their own <script> and <link> tags, once per page however many
you use.

3. PREREQUISITES IN YOUR PAGE OR MASTER PAGE
--------------------------------------------
  - Bootstrap 5 CSS. Only class names are used, so it can sit alongside older
    Bootstrap JavaScript. The Two-Factor control also needs Bootstrap's JS for its
    confirmation modal.
  - Bootstrap Icons, for the provider and status icons.
  - Session state enabled (the default). Tokens are stored as strings, so
    StateServer and SQLServer session modes work as well as InProc.

RECOMMENDED, NOT REQUIRED
-------------------------
Add Async="true" to the @Page directive of pages hosting a Wildwood control:

  <%@ Page Language="C#" Async="true" ... %>

The controls read from the API while rendering. On an async page that read is
awaited properly by the framework; without it the control still works, using a
thread-pool fallback that cannot deadlock, but the async form is more efficient.

CUSTOMISING THE MARKUP
----------------------
Do not edit the shipped .ascx files in place. A NuGet update replaces files it
still recognises and silently KEEPS ones you have changed, so an edited file would
stay frozen at the version you edited. Instead:

  1. Copy the .ascx to your own folder.
  2. Point your @Register Src= at the copy.
  3. Leave the Inherits= attribute alone: it names the compiled class that gives
     the markup its behaviour.

BINDING REDIRECTS
-----------------
This package uses System.Text.Json, which on .NET Framework brings System.Memory,
System.Buffers, System.Runtime.CompilerServices.Unsafe and friends. Visual Studio
normally writes the required <bindingRedirect> entries into web.config for you. If
you see a TypeLoadException or "Could not load file or assembly ... System.Runtime
.CompilerServices.Unsafe", run this in the Package Manager Console:

  Add-BindingRedirect

TROUBLESHOOTING
---------------
  Nothing renders, or "BaseUrl is not configured"
      WildwoodAPI:BaseUrl is missing from <appSettings>.

  Everything renders but no call reaches the API
      Check WildwoodAPI:ApiKey and WildwoodAPI:AppId are filled in.

  You need the token module out of the request pipeline while diagnosing
      <add key="WildwoodAPI:DisableTokenModule" value="true" />

  Diagnostics
      The package writes to System.Diagnostics.Trace under the "Wildwood" category.
      Route it wherever you like with <system.diagnostics>, or assign your own sink
      to WildwoodWebForms.Logger in Application_Start.

KNOWN LIMITATIONS
-----------------
  - Controls are not supported inside an UpdatePanel. A partial postback re-renders
    the markup without re-running the scripts that bind to it, leaving the new DOM
    inert. Place Wildwood controls outside any UpdatePanel.
  - The Authentication control uses fixed element ids, so only one instance belongs
    on a page. The Two-Factor control may be used more than once.

DOCUMENTATION
-------------
  https://admin.wildwoodworks.io
