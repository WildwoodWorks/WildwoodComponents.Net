<%--
    Wildwood Two-Factor Settings control.

    Inherits from a compiled type in WildwoodComponents.WebForms and has no CodeFile, so
    the ASP.NET runtime compiles this markup against that base class: there is nothing
    here for you to build.

        <%@ Register TagPrefix="ww" TagName="TwoFactorSettings"
                     Src="~/Controls/Wildwood/TwoFactorSettingsControl.ascx" %>

        <ww:TwoFactorSettings runat="server" />

    Customising: copy this file rather than editing it in place. A NuGet update replaces
    files it still recognises and silently keeps ones you changed, so an edited copy here
    would freeze at this version. Point your Src= at the copy and leave Inherits= alone.

    Unlike the Authentication control this one may appear more than once on a page: every
    id is suffixed with a per-instance component id.

    Requires Bootstrap 5 (CSS for layout, and its JS for the confirmation modal) and
    Bootstrap Icons.
--%>
<%@ Control Language="C#" Inherits="WildwoodComponents.WebForms.Controls.TwoFactorSettingsControlBase" %>

<div id="<%= Attr(Id("ww-2fa-settings-")) %>"
     class="ww-2fa-settings-component"
     data-component-id="<%= Attr(ComponentId) %>"
     data-proxy-url="<%= Attr(ResolvedProxyUrl) %>"
     data-is-enabled="<%= Attr(IsEnabled) %>">

    <%-- Error/success message area --%>
    <div id="<%= Attr(Id("ww-2fa-message-")) %>" class="ww-alert" style="display:none;"></div>

    <%-- ===== STATUS HEADER ===== --%>
    <div class="ww-2fa-status-header mb-4">
        <div class="d-flex align-items-center justify-content-between">
            <div>
                <h4 class="mb-1">Two-Factor Authentication</h4>
                <p class="text-muted mb-0">
                    <% if (IsEnabled) { %>
                    <span class="badge bg-success me-2">Enabled</span>
                    <span><%= MethodCount %> method(s) configured</span>
                    <% } else { %>
                    <span class="badge bg-warning text-dark me-2">Disabled</span>
                    <span>Add a method to enable two-factor authentication</span>
                    <% } %>
                </p>
            </div>
            <% if (IsRequired) { %>
            <span class="badge bg-danger">Required</span>
            <% } %>
        </div>
    </div>

    <%-- ===== AUTHENTICATOR APP ===== --%>
    <div class="ww-2fa-section card mb-3">
        <div class="card-header d-flex align-items-center justify-content-between">
            <h5 class="mb-0">
                <i class="bi bi-phone me-2"></i>Authenticator App
            </h5>
            <button type="button"
                    class="btn btn-sm btn-outline-primary ww-2fa-add-authenticator-btn"
                    data-action="begin-authenticator">
                <i class="bi bi-plus-lg me-1"></i>Add Authenticator
            </button>
        </div>
        <div class="card-body">
            <p class="text-muted mb-3">Use an authenticator app like Google Authenticator or Authy to generate verification codes.</p>

            <div id="<%= Attr(Id("ww-2fa-authenticator-enroll-")) %>" class="ww-2fa-enroll-panel" style="display:none;">
                <div class="mb-3">
                    <label for="<%= Attr(Id("ww-2fa-authenticator-name-")) %>" class="form-label">Friendly Name (optional)</label>
                    <input type="text" class="form-control" id="<%= Attr(Id("ww-2fa-authenticator-name-")) %>"
                           placeholder="e.g., My Phone" />
                </div>

                <div id="<%= Attr(Id("ww-2fa-qr-area-")) %>" class="text-center mb-3" style="display:none;">
                    <p class="fw-bold">Scan this QR code with your authenticator app:</p>
                    <div class="ww-2fa-qr-container d-inline-block p-3 bg-white rounded border">
                        <img id="<%= Attr(Id("ww-2fa-qr-img-")) %>" src="" alt="QR Code" style="max-width: 200px; height: auto;" />
                    </div>
                    <div class="mt-2">
                        <button type="button" class="btn btn-link btn-sm ww-2fa-toggle-manual" data-target="<%= Attr(Id("ww-2fa-manual-key-")) %>">
                            Can't scan? Enter key manually
                        </button>
                    </div>
                    <div id="<%= Attr(Id("ww-2fa-manual-key-")) %>" class="mt-2" style="display:none;">
                        <code id="<%= Attr(Id("ww-2fa-manual-key-value-")) %>" class="d-block p-2 bg-light rounded user-select-all"></code>
                    </div>
                </div>

                <div id="<%= Attr(Id("ww-2fa-authenticator-verify-")) %>" style="display:none;">
                    <div class="mb-3">
                        <label for="<%= Attr(Id("ww-2fa-authenticator-code-")) %>" class="form-label">Verification Code</label>
                        <input type="text" class="form-control" id="<%= Attr(Id("ww-2fa-authenticator-code-")) %>"
                               maxlength="6" pattern="[0-9]{6}" inputmode="numeric"
                               placeholder="Enter 6-digit code" autocomplete="one-time-code" />
                        <div class="invalid-feedback">Please enter a valid 6-digit code.</div>
                    </div>
                    <input type="hidden" id="<%= Attr(Id("ww-2fa-authenticator-credential-id-")) %>" value="" />
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-primary ww-2fa-verify-authenticator-btn">
                            <span class="ww-btn-text">Verify &amp; Enable</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Verifying...
                            </span>
                        </button>
                        <button type="button" class="btn btn-outline-secondary ww-2fa-cancel-enroll-btn">Cancel</button>
                    </div>
                </div>

                <div id="<%= Attr(Id("ww-2fa-authenticator-begin-")) %>">
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-primary ww-2fa-begin-authenticator-btn">
                            <span class="ww-btn-text">Generate QR Code</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Generating...
                            </span>
                        </button>
                        <button type="button" class="btn btn-outline-secondary ww-2fa-cancel-enroll-btn">Cancel</button>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <%-- ===== EMAIL 2FA ===== --%>
    <div class="ww-2fa-section card mb-3">
        <div class="card-header d-flex align-items-center justify-content-between">
            <h5 class="mb-0">
                <i class="bi bi-envelope me-2"></i>Email Verification
            </h5>
            <button type="button"
                    class="btn btn-sm btn-outline-primary ww-2fa-add-email-btn"
                    data-action="begin-email">
                <i class="bi bi-plus-lg me-1"></i>Add Email
            </button>
        </div>
        <div class="card-body">
            <p class="text-muted mb-3">Receive a verification code via email when signing in.</p>

            <div id="<%= Attr(Id("ww-2fa-email-enroll-")) %>" class="ww-2fa-enroll-panel" style="display:none;">
                <div class="mb-3">
                    <label for="<%= Attr(Id("ww-2fa-email-address-")) %>" class="form-label">Email Address (optional, uses account email if blank)</label>
                    <input type="email" class="form-control" id="<%= Attr(Id("ww-2fa-email-address-")) %>"
                           placeholder="Leave blank to use your account email" />
                </div>

                <div id="<%= Attr(Id("ww-2fa-email-verify-")) %>" style="display:none;">
                    <p class="text-muted">A verification code has been sent to <strong id="<%= Attr(Id("ww-2fa-email-sent-to-")) %>"></strong>.</p>
                    <div class="mb-3">
                        <label for="<%= Attr(Id("ww-2fa-email-code-")) %>" class="form-label">Verification Code</label>
                        <input type="text" class="form-control" id="<%= Attr(Id("ww-2fa-email-code-")) %>"
                               maxlength="6" pattern="[0-9]{6}" inputmode="numeric"
                               placeholder="Enter 6-digit code" autocomplete="one-time-code" />
                        <div class="invalid-feedback">Please enter a valid 6-digit code.</div>
                    </div>
                    <input type="hidden" id="<%= Attr(Id("ww-2fa-email-credential-id-")) %>" value="" />
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-primary ww-2fa-verify-email-btn">
                            <span class="ww-btn-text">Verify</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Verifying...
                            </span>
                        </button>
                        <button type="button" class="btn btn-outline-secondary ww-2fa-cancel-enroll-btn">Cancel</button>
                    </div>
                </div>

                <div id="<%= Attr(Id("ww-2fa-email-begin-")) %>">
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-primary ww-2fa-send-email-btn">
                            <span class="ww-btn-text">Send Verification Code</span>
                            <span class="ww-btn-spinner" style="display:none;">
                                <span class="ww-spinner"></span> Sending...
                            </span>
                        </button>
                        <button type="button" class="btn btn-outline-secondary ww-2fa-cancel-enroll-btn">Cancel</button>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <%-- ===== ACTIVE METHODS ===== --%>
    <div class="ww-2fa-section card mb-3">
        <div class="card-header">
            <h5 class="mb-0">
                <i class="bi bi-shield-check me-2"></i>Active Methods
            </h5>
        </div>
        <div class="card-body">
            <% if (Credentials.Count == 0) { %>
            <p class="text-muted mb-0">No two-factor methods configured.</p>
            <% } else { %>
            <div class="table-responsive">
                <table class="table table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Method</th>
                            <th>Name</th>
                            <th>Status</th>
                            <th>Last Used</th>
                            <th class="text-end">Actions</th>
                        </tr>
                    </thead>
                    <tbody id="<%= Attr(Id("ww-2fa-credentials-list-")) %>">
                        <% foreach (var cred in Credentials) { %>
                        <tr data-credential-id="<%= Attr(cred.Id) %>">
                            <td>
                                <i class="bi <%= Attr(CredentialIcon(cred.ProviderType)) %> me-1"></i>
                                <%= Text(cred.ProviderType) %>
                            </td>
                            <td>
                                <%= Text(CredentialName(cred)) %>
                                <% if (cred.IsPrimary) { %>
                                <span class="badge bg-primary ms-1">Primary</span>
                                <% } %>
                            </td>
                            <td>
                                <% if (cred.IsVerified && cred.IsActive) { %>
                                <span class="badge bg-success">Active</span>
                                <% } else if (!cred.IsVerified) { %>
                                <span class="badge bg-warning text-dark">Unverified</span>
                                <% } else { %>
                                <span class="badge bg-secondary">Inactive</span>
                                <% } %>
                            </td>
                            <td>
                                <% if (cred.LastUsedAt.HasValue) { %>
                                <span title="<%= Attr(LongDate(cred.LastUsedAt)) %>"><%= Text(ShortDate(cred.LastUsedAt)) %></span>
                                <% } else { %>
                                <span class="text-muted">Never</span>
                                <% } %>
                            </td>
                            <td class="text-end">
                                <% if (!cred.IsPrimary) { %>
                                <button type="button" class="btn btn-sm btn-outline-primary me-1 ww-2fa-set-primary-btn"
                                        data-credential-id="<%= Attr(cred.Id) %>" title="Set as primary">
                                    <i class="bi bi-star"></i>
                                </button>
                                <% } %>
                                <button type="button" class="btn btn-sm btn-outline-danger ww-2fa-remove-btn"
                                        data-credential-id="<%= Attr(cred.Id) %>" title="Remove">
                                    <i class="bi bi-trash"></i>
                                </button>
                            </td>
                        </tr>
                        <% } %>
                    </tbody>
                </table>
            </div>
            <% } %>
        </div>
    </div>

    <%-- ===== TRUSTED DEVICES ===== --%>
    <div class="ww-2fa-section card mb-3">
        <div class="card-header d-flex align-items-center justify-content-between">
            <h5 class="mb-0">
                <i class="bi bi-laptop me-2"></i>Trusted Devices
            </h5>
            <% if (TrustedDevices.Count > 0) { %>
            <button type="button" class="btn btn-sm btn-outline-danger ww-2fa-revoke-all-devices-btn">
                <i class="bi bi-x-circle me-1"></i>Revoke All
            </button>
            <% } %>
        </div>
        <div class="card-body">
            <% if (TrustedDevices.Count == 0) { %>
            <p class="text-muted mb-0">No trusted devices.</p>
            <% } else { %>
            <div class="table-responsive">
                <table class="table table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Device</th>
                            <th>Location</th>
                            <th>Last Used</th>
                            <th>Expires</th>
                            <th class="text-end">Actions</th>
                        </tr>
                    </thead>
                    <tbody id="<%= Attr(Id("ww-2fa-devices-list-")) %>">
                        <% foreach (var device in TrustedDevices) { %>
                        <tr data-device-id="<%= Attr(device.Id) %>" class="<%= device.IsExpired ? "table-secondary" : "" %>">
                            <td>
                                <i class="bi bi-display me-1"></i>
                                <%= Text(string.IsNullOrEmpty(device.DeviceName) ? "Unknown Device" : device.DeviceName) %>
                            </td>
                            <td><%= Text(string.IsNullOrEmpty(device.Location) ? "—" : device.Location) %></td>
                            <td>
                                <% if (device.LastUsedAt.HasValue) { %>
                                <span title="<%= Attr(LongDate(device.LastUsedAt)) %>"><%= Text(ShortDate(device.LastUsedAt)) %></span>
                                <% } else { %>
                                <span class="text-muted">Never</span>
                                <% } %>
                            </td>
                            <td>
                                <% if (device.IsExpired) { %>
                                <span class="badge bg-danger">Expired</span>
                                <% } else if (device.ExpiresAt != default(System.DateTime)) { %>
                                <span title="<%= Attr(LongDate(device.ExpiresAt)) %>"><%= Text(ShortDate(device.ExpiresAt)) %></span>
                                <% } else { %>
                                <span class="text-muted">No expiry</span>
                                <% } %>
                            </td>
                            <td class="text-end">
                                <button type="button" class="btn btn-sm btn-outline-danger ww-2fa-revoke-device-btn"
                                        data-device-id="<%= Attr(device.Id) %>" title="Revoke">
                                    <i class="bi bi-x-lg"></i>
                                </button>
                            </td>
                        </tr>
                        <% } %>
                    </tbody>
                </table>
            </div>
            <% } %>
        </div>
    </div>

    <%-- ===== RECOVERY CODES ===== --%>
    <div class="ww-2fa-section card mb-3">
        <div class="card-header d-flex align-items-center justify-content-between">
            <h5 class="mb-0">
                <i class="bi bi-key me-2"></i>Recovery Codes
            </h5>
            <% if (IsEnabled) { %>
            <button type="button" class="btn btn-sm btn-outline-warning ww-2fa-regenerate-codes-btn">
                <i class="bi bi-arrow-clockwise me-1"></i>Regenerate
            </button>
            <% } %>
        </div>
        <div class="card-body">
            <% if (RecoveryCodeInfo != null) { %>
            <div class="d-flex align-items-center gap-3 mb-2">
                <div>
                    <span class="fw-bold fs-4"><%= RecoveryCodeInfo.Remaining %></span>
                    <span class="text-muted">/ <%= RecoveryCodeInfo.TotalGenerated %> remaining</span>
                </div>
                <% if (RecoveryCodeInfo.Remaining <= 2) { %>
                <span class="badge bg-danger">Low</span>
                <% } %>
            </div>
            <% if (RecoveryCodeInfo.GeneratedAt.HasValue) { %>
            <p class="text-muted small mb-0">
                Generated on <%= Text(RecoveryCodeInfo.GeneratedAt.Value.ToString("MMMM d, yyyy 'at' h:mm tt")) %>
            </p>
            <% } %>
            <% } else { %>
            <p class="text-muted mb-0">
                <% if (IsEnabled) { %>
                <span>No recovery codes generated. Click <strong>Regenerate</strong> to create new codes.</span>
                <% } else { %>
                <span>Enable two-factor authentication to generate recovery codes.</span>
                <% } %>
            </p>
            <% } %>

            <div id="<%= Attr(Id("ww-2fa-recovery-codes-display-")) %>" style="display:none;" class="mt-3">
                <div class="alert alert-warning">
                    <i class="bi bi-exclamation-triangle me-2"></i>
                    <strong>Save these codes!</strong> Each code can only be used once. Store them in a safe place.
                </div>
                <div id="<%= Attr(Id("ww-2fa-recovery-codes-list-")) %>" class="row row-cols-2 g-2 mb-3"></div>
                <button type="button" class="btn btn-sm btn-outline-secondary ww-2fa-copy-codes-btn">
                    <i class="bi bi-clipboard me-1"></i>Copy All Codes
                </button>
            </div>
        </div>
    </div>

    <%-- ===== CONFIRMATION MODAL ===== --%>
    <div class="modal fade" id="<%= Attr(Id("ww-2fa-confirm-modal-")) %>" tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title" id="<%= Attr(Id("ww-2fa-confirm-title-")) %>">Confirm Action</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <p id="<%= Attr(Id("ww-2fa-confirm-message-")) %>"></p>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="button" class="btn btn-danger" id="<%= Attr(Id("ww-2fa-confirm-action-")) %>">
                        <span class="ww-btn-text">Confirm</span>
                        <span class="ww-btn-spinner" style="display:none;">
                            <span class="ww-spinner"></span> Processing...
                        </span>
                    </button>
                </div>
            </div>
        </div>
    </div>
</div>
