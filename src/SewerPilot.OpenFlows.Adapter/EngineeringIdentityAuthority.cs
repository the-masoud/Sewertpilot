using System;
using System.Linq;
using System.Security.Principal;

namespace SewerPilot.OpenFlows.Adapter
{
    // This is the bounded authority library, not a deployed privileged service or an IPC authentication platform.
    // Its verifiers are deliberately TEST_ONLY until a separately secured service/runtime composition is established.
    public sealed class EngineeringIdentityAuthority : IGravityEngineeringApprovalVerifier
    {
        private readonly WindowsEngineeringIdentityStore store;
        private readonly EngineeringIdentityPolicy policy;
        private readonly ActiveDirectoryIdentityConfiguration ad;
        private readonly IEngineeringDirectory directory;
        private readonly Func<DateTimeOffset> clock;
        public string RuntimeTrustBoundaryStatus => "BLOCKED_INDEPENDENT_AUTHORITY_NOT_DEPLOYED";
        public EngineeringIdentityAuthority(WindowsEngineeringIdentityStore store, EngineeringIdentityPolicy policy,
            ActiveDirectoryIdentityConfiguration ad = null) : this(store, policy, ad, new WindowsLdapsEngineeringDirectory(), () => DateTimeOffset.UtcNow) { }
        internal EngineeringIdentityAuthority(WindowsEngineeringIdentityStore store, EngineeringIdentityPolicy policy,
            ActiveDirectoryIdentityConfiguration ad, IEngineeringDirectory directory, Func<DateTimeOffset> clock)
        {
            IdentityGuard.Require(store != null && policy != null && directory != null && clock != null, "IDENTITY_AUTHORITY_CONFIGURATION_MISSING");
            this.store = store; this.policy = Clone(policy); this.policy.Validate();
            this.ad = ad == null ? null : Clone(ad); this.ad?.Validate(); this.directory = directory; this.clock = clock;
        }
        private static T Clone<T>(T input) => RuntimeHostProtocol.Serializer().Deserialize<T>(RuntimeHostProtocol.Serializer().Serialize(input));
        private void Audit(EngineeringIdentityState state, string action, string actor = null, string project = null, string reference = null)
        {
            state.Audit.Add(new AuditRecord { AuditRecordId = Guid.NewGuid().ToString("D"), Sequence = state.Audit.Count + 1,
                TimestampUtc = clock().ToString("o"), ProjectId = project, Module = "EngineeringIdentity", ActionType = action,
                CorrelationId = reference, Payload = new System.Collections.Generic.Dictionary<string, string> { { "IdentityKey", actor ?? "UNAUTHENTICATED" } } });
        }
        private T Operation<T>(string action, Func<EngineeringIdentityState, T> operation)
        {
            // Persist security counters/rejections even when the operation is denied. An audit/storage failure denies the operation.
            EngineeringIdentityException rejection = null;
            T result = store.Update(s => {
                try { return operation(s); }
                catch (EngineeringIdentityException error) { rejection = error; Audit(s, action + ".Rejected", reference: error.Message); return default(T); }
                catch { rejection = new EngineeringIdentityException("IDENTITY_PROVIDER_FAILURE"); Audit(s, action + ".Rejected", reference: rejection.Message); return default(T); }
            });
            if (rejection != null) throw rejection;
            return result;
        }
        private EngineeringLocalAccount Account(EngineeringIdentityState state, string identity) => state.Accounts.SingleOrDefault(a => "LOCAL:" + a.Id == identity);
        private EngineeringStoredSession Session(EngineeringIdentityState state, EngineeringIdentitySession supplied)
        {
            var stored = supplied == null ? null : state.Sessions.SingleOrDefault(s => s.Id == supplied.Id);
            IdentityGuard.Require(stored != null && !stored.Revoked && stored.ExpiresUtc > clock() && stored.IdentityKey == supplied.IdentityKey &&
                stored.Provider == supplied.Provider && stored.TokenHash == IdentityGuard.Hash(supplied.Token), "AUTHENTICATED_SESSION_REQUIRED");
            return stored;
        }
        private EngineeringDirectoryIdentity CheckDirectoryIdentity(EngineeringDirectoryIdentity result, string expectedSid = null)
        {
            IdentityGuard.Require(result != null && result.Enabled && result.MembershipVerified && IdentityGuard.ValidSid(result.Sid) &&
                (expectedSid == null || result.Sid == expectedSid) && result.SecurityGroups != null && result.SecurityGroups.Length > 0 &&
                result.SecurityGroups.All(IdentityGuard.ValidSid), "AD_MEMBERSHIP_OR_IDENTITY_UNVERIFIED");
            IdentityGuard.Require(ad != null && ad.GroupRoleMappings.Any(m => result.SecurityGroups.Contains(m.GroupSid)), "AD_GROUP_NOT_PERMITTED");
            return result;
        }
        private bool Permitted(EngineeringIdentityState state, EngineeringStoredSession session, string project, EngineeringPermission permission)
        {
            if (session == null || session.Revoked || session.ExpiresUtc <= clock() || string.IsNullOrWhiteSpace(project)) return false;
            if (session.Provider == EngineeringIdentityProvider.Local)
            {
                var account = Account(state, session.IdentityKey);
                if (account == null || account.Disabled || account.LockedUntil > clock() ||
                    (session.Emergency && (!policy.LocalEmergencyAccessEnabled || !account.EmergencyAuthorized))) return false;
                return state.Grants.Any(g => g.IdentityKey == session.IdentityKey && g.ProjectId == project && EngineeringRbac.Allows(g.Role, permission));
            }
            IdentityGuard.Require(session.Provider == EngineeringIdentityProvider.ActiveDirectory && ad != null && session.IdentityKey.StartsWith("AD:", StringComparison.Ordinal), "PROVIDER_SELECTION_INVALID");
            string sid = session.IdentityKey.Substring(3);
            var identity = CheckDirectoryIdentity(directory.Revalidate(ad, sid), sid);
            // Only current verified directory security-group mappings grant AD roles; local grants never fall back.
            return ad.GroupRoleMappings.Any(m => m.ProjectId == project && identity.SecurityGroups.Contains(m.GroupSid) && EngineeringRbac.Allows(m.Role, permission));
        }
        private void Manage(EngineeringIdentityState state, EngineeringIdentitySession session)
        { IdentityGuard.Require(Permitted(state, Session(state, session), policy.IdentityAdministrationProjectId, EngineeringPermission.ManageIdentities), "IDENTITY_ADMINISTRATOR_REQUIRED"); }
        public string BootstrapAdministrator(string username, string password, string explicitlyAuthorizedWindowsSid)
        {
            return Operation("AdministratorBootstrap", s => {
                IdentityGuard.Require(IdentityGuard.ValidSid(explicitlyAuthorizedWindowsSid) && WindowsIdentity.GetCurrent().User.Value == explicitlyAuthorizedWindowsSid &&
                    new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), "BOOTSTRAP_ADMINISTRATOR_DENIED");
                IdentityGuard.Require(s.Accounts.Count == 0 && s.Grants.Count == 0 && s.Approvals.Count == 0, "BOOTSTRAP_ALREADY_ENROLLED");
                string id = Enroll(s, username, password, false);
                s.Grants.Add(new EngineeringRoleGrant { IdentityKey = id, ProjectId = policy.IdentityAdministrationProjectId, Role = EngineeringProjectRole.Administrator });
                Audit(s, "AdministratorBootstrap", id, policy.IdentityAdministrationProjectId); return id;
            });
        }
        // Internal deterministic fixture enrollment is TEST_ONLY and can never enable live RuntimeHost authorization.
        internal string EnrollTestAdministrator(string username, string password)
        {
            IdentityGuard.Require(policy.IdentityAdministrationProjectId.StartsWith("TEST_ONLY_NON_ENGINEERING", StringComparison.Ordinal), "TEST_ONLY_REQUIRED");
            return Operation("TestEnrollment", s => {
                IdentityGuard.Require(s.Accounts.Count == 0, "BOOTSTRAP_ALREADY_ENROLLED");
                var id = Enroll(s, username, password, false);
                s.Grants.Add(new EngineeringRoleGrant { IdentityKey = id, ProjectId = policy.IdentityAdministrationProjectId, Role = EngineeringProjectRole.Administrator });
                Audit(s, "TEST_ONLY_NON_ENGINEERING.Enrollment", id); return id;
            });
        }
        private string Enroll(EngineeringIdentityState state, string username, string password, bool emergency)
        {
            string normalized = IdentityGuard.Username(username);
            IdentityGuard.Require(password != null && password.Length >= policy.Passwords.MinimumLength && password.Length <= policy.Passwords.MaximumLength, "PASSWORD_POLICY_REJECTED");
            IdentityGuard.Require(!state.Accounts.Any(a => a.Username == normalized), "USERNAME_ALREADY_ENROLLED");
            string salt = Convert.ToBase64String(IdentityGuard.Random(32)), id = Guid.NewGuid().ToString("D");
            state.Accounts.Add(new EngineeringLocalAccount { Id = id, Username = normalized, Salt = salt,
                PasswordHash = IdentityGuard.PasswordHash(password, salt, policy.Passwords.Pbkdf2Iterations), Iterations = policy.Passwords.Pbkdf2Iterations,
                EmergencyAuthorized = emergency });
            return "LOCAL:" + id;
        }
        public string CreateLocalAccount(EngineeringIdentitySession administrator, string username, string password, bool authorizeEmergency = false)
        {
            return Operation("AccountEnrollment", s => { Manage(s, administrator); string id = Enroll(s, username, password, authorizeEmergency);
                Audit(s, "AccountEnrollment", administrator.IdentityKey, policy.IdentityAdministrationProjectId, id); return id; });
        }
        public void SetLocalAccountDisabled(EngineeringIdentitySession administrator, string identity, bool disabled)
        {
            Operation("AccountStatus", s => { Manage(s, administrator); var account = Account(s, identity); IdentityGuard.Require(account != null, "LOCAL_ACCOUNT_MISSING");
                account.Disabled = disabled;
                if (disabled) foreach (var session in s.Sessions.Where(t => t.IdentityKey == identity)) session.Revoked = true;
                Audit(s, disabled ? "AccountDisabled" : "AccountEnabled", administrator.IdentityKey, reference: identity); return true; });
        }
        public void SetLocalRole(EngineeringIdentitySession administrator, string identity, string project, EngineeringProjectRole role, bool grant)
        {
            Operation("ProjectRole", s => { Manage(s, administrator); IdentityGuard.Require(Account(s, identity) != null && !string.IsNullOrWhiteSpace(project) &&
                    Enum.IsDefined(typeof(EngineeringProjectRole), role), "ROLE_GRANT_INVALID");
                s.Grants.RemoveAll(g => g.IdentityKey == identity && g.ProjectId == project && g.Role == role);
                if (grant) s.Grants.Add(new EngineeringRoleGrant { IdentityKey = identity, ProjectId = project, Role = role });
                Audit(s, grant ? "RoleGranted" : "RoleRevoked", administrator.IdentityKey, project, identity); return true; });
        }
        public EngineeringIdentitySession Authenticate(EngineeringIdentityProvider selectedProvider, string username, string password, bool requestEmergencyAccess = false)
        {
            return Operation("Authentication", s => {
                IdentityGuard.Require(Enum.IsDefined(typeof(EngineeringIdentityProvider), selectedProvider), "PROVIDER_SELECTION_REQUIRED");
                IdentityGuard.Require(s.AuthenticationLockedUntil <= clock(), "AUTHENTICATION_RATE_LIMITED");
                string identity;
                try
                {
                    if (selectedProvider == EngineeringIdentityProvider.Local)
                    {
                        var name = IdentityGuard.Username(username); var account = s.Accounts.SingleOrDefault(a => a.Username == name);
                        IdentityGuard.Require(account != null && !account.Disabled && account.LockedUntil <= clock(), "LOCAL_AUTHENTICATION_REJECTED");
                        bool valid = password != null && password.Length <= policy.Passwords.MaximumLength &&
                            IdentityGuard.Equal(Convert.FromBase64String(account.PasswordHash), Convert.FromBase64String(IdentityGuard.PasswordHash(password, account.Salt, account.Iterations)));
                        if (!valid)
                        {
                            account.Failures++;
                            if (account.Failures >= policy.Passwords.MaximumFailures)
                            { account.LockedUntil = clock().AddMinutes(policy.Passwords.LockoutMinutes).UtcDateTime; account.Failures = 0; }
                            throw new EngineeringIdentityException("LOCAL_AUTHENTICATION_REJECTED");
                        }
                        IdentityGuard.Require(!requestEmergencyAccess || (policy.LocalEmergencyAccessEnabled && account.EmergencyAuthorized), "LOCAL_EMERGENCY_ACCESS_DENIED");
                        account.Failures = 0; identity = "LOCAL:" + account.Id;
                    }
                    else
                    {
                        IdentityGuard.Require(ad != null && !requestEmergencyAccess, "AD_NOT_CONFIGURED");
                        var result = CheckDirectoryIdentity(directory.Authenticate(ad, username, password)); identity = "AD:" + result.Sid;
                    }
                }
                catch
                {
                    s.AuthenticationFailures++;
                    if (s.AuthenticationFailures >= policy.Passwords.MaximumFailures)
                    { s.AuthenticationLockedUntil = clock().AddMinutes(policy.Passwords.LockoutMinutes).UtcDateTime; s.AuthenticationFailures = 0; }
                    throw;
                }
                // Reset only after successful authentication; a per-authority persisted limit bounds unknown-user/password spraying.
                s.AuthenticationFailures = 0;
                string id = Guid.NewGuid().ToString("D"), token = Convert.ToBase64String(IdentityGuard.Random(32)); var expiry = clock().AddMinutes(policy.SessionMinutes);
                s.Sessions.Add(new EngineeringStoredSession { Id = id, IdentityKey = identity, Provider = selectedProvider,
                    TokenHash = IdentityGuard.Hash(token), AuthenticatedUtc = clock().UtcDateTime, ExpiresUtc = expiry.UtcDateTime, Emergency = requestEmergencyAccess });
                Audit(s, requestEmergencyAccess ? "EmergencyAuthentication" : "Authentication", identity, reference: id);
                return new EngineeringIdentitySession(id, token, identity, selectedProvider, expiry);
            });
        }
        public void RevokeSession(EngineeringIdentitySession session) => Operation("SessionRevocation", s => {
            var current = Session(s, session); current.Revoked = true; Audit(s, "SessionRevoked", current.IdentityKey, reference: current.Id); return true; });
        public bool HasPermission(EngineeringIdentitySession session, string project, EngineeringPermission permission) =>
            Operation("PermissionCheck", s => { var current = Session(s, session); bool allowed = Permitted(s, current, project, permission);
                Audit(s, allowed ? "PermissionAllowed" : "PermissionDenied", current.IdentityKey, project); return allowed; });
        public void IssueApproval(EngineeringIdentitySession approver, EngineeringIdentitySession submitter, GravityExecutionPlan supplied, DateTimeOffset validUntilUtc)
        {
            Operation("ApprovalIssuance", s => {
                var a = Session(s, approver); var submitted = Session(s, submitter);
                var plan = RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(GravityExecutionPlanBuilder.Json(supplied));
                GravityExecutionPlanBuilder.Validate(plan); var binding = new GravityEngineeringApprovalBinding(plan);
                IdentityGuard.Require(binding.ActorId == a.IdentityKey && binding.ExecuteEngineeringDesign &&
                    Permitted(s, a, binding.ProjectId, EngineeringPermission.ApproveCandidate) &&
                    Permitted(s, a, binding.ProjectId, EngineeringPermission.AuthorizeExecution) &&
                    Permitted(s, submitted, binding.ProjectId, EngineeringPermission.EditDesignBasis), "ENGINEERING_APPROVAL_PERMISSION_DENIED");
                IdentityGuard.Require(a.IdentityKey != submitted.IdentityKey || policy.AllowExceptionalSelfApproval, "SELF_APPROVAL_DENIED");
                var approvedUtc = DateTimeOffset.Parse(binding.ApprovedUtc, System.Globalization.CultureInfo.InvariantCulture);
                IdentityGuard.Require(approvedUtc >= a.AuthenticatedUtc && approvedUtc <= clock(), "ENGINEERING_APPROVAL_STALE");
                IdentityGuard.Require(validUntilUtc > clock() && validUntilUtc <= a.ExpiresUtc &&
                    !s.Approvals.Any(p => p.ApprovalId == binding.ApprovalId), "APPROVAL_VALIDITY_OR_ID_CONFLICT");
                s.Approvals.Add(new EngineeringStoredApproval { ApprovalId = binding.ApprovalId, BindingJson = GravityExecutionPlanBuilder.Json(binding),
                    ApproverSessionId = a.Id, SubmitterIdentity = submitted.IdentityKey, IssuedUtc = clock().UtcDateTime, ValidUntilUtc = validUntilUtc.UtcDateTime });
                Audit(s, a.IdentityKey == submitted.IdentityKey ? "ExceptionalSelfApprovalIssued" : "ApprovalIssued", a.IdentityKey, binding.ProjectId, binding.ApprovalId);
                if (a.IdentityKey == submitted.IdentityKey) s.Audit.Last().Payload.Add("SelfApprovalPolicyReference", policy.SelfApprovalPolicyReference);
                return true;
            });
        }
        public void RevokeApproval(EngineeringIdentitySession administratorOrApprover, string approvalId)
        {
            Operation("ApprovalRevocation", s => {
                var current = Session(s, administratorOrApprover); var record = s.Approvals.SingleOrDefault(a => a.ApprovalId == approvalId);
                IdentityGuard.Require(record != null, "APPROVAL_NOT_FOUND");
                var binding = RuntimeHostProtocol.Serializer().DeserializeObject(record.BindingJson) as System.Collections.Generic.Dictionary<string, object>;
                string project = (string)binding["ProjectId"];
                IdentityGuard.Require(Permitted(s, current, project, EngineeringPermission.ApproveCandidate) ||
                    Permitted(s, current, policy.IdentityAdministrationProjectId, EngineeringPermission.ManageIdentities), "APPROVAL_REVOCATION_DENIED");
                record.Revoked = true; Audit(s, "ApprovalRevoked", current.IdentityKey, project, approvalId); return true;
            });
        }
        public GravityEngineeringApprovalVerification Verify(GravityEngineeringApprovalBinding binding)
        {
            try
            {
                return Operation("ApprovalVerification", s => {
                    IdentityGuard.Require(binding != null, "APPROVAL_BINDING_REQUIRED");
                    var record = s.Approvals.SingleOrDefault(a => a.ApprovalId == binding.ApprovalId);
                    IdentityGuard.Require(record != null && !record.Revoked && record.ValidUntilUtc > clock() &&
                        record.BindingJson == GravityExecutionPlanBuilder.Json(binding), "AUTHORITATIVE_APPROVAL_REJECTED");
                    var actor = s.Sessions.SingleOrDefault(t => t.Id == record.ApproverSessionId);
                    IdentityGuard.Require(actor != null && actor.IdentityKey == binding.ActorId &&
                        Permitted(s, actor, binding.ProjectId, EngineeringPermission.ApproveCandidate) &&
                        Permitted(s, actor, binding.ProjectId, EngineeringPermission.AuthorizeExecution), "AUTHORITATIVE_APPROVER_UNAUTHORIZED");
                    Audit(s, "ApprovalVerified", binding.ActorId, binding.ProjectId, binding.ApprovalId);
                    return new GravityEngineeringApprovalVerification(true, binding.PlanFingerprint, binding.DecisionReference, record.ValidUntilUtc,
                        RuntimeTrustBoundaryStatus, testOnly: true);
                });
            }
            catch (EngineeringIdentityException e)
            { return new GravityEngineeringApprovalVerification(false, null, null, DateTimeOffset.MinValue, e.Message, testOnly: true); }
        }
    }
}
