using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private sealed class IdentityDirectoryDouble : IEngineeringDirectory
        {
            internal string Failure;
            internal bool MembershipVerified = true, Enabled = true;
            internal string Sid = "S-1-5-21-111-222-333-1001";
            internal string Group = "S-1-5-21-111-222-333-2001";
            internal int Authentications, Revalidations;
            private EngineeringDirectoryIdentity Read()
            {
                if (Failure != null) throw new EngineeringIdentityException(Failure);
                return new EngineeringDirectoryIdentity { Sid = Sid, SecurityGroups = new[] { Group }, Enabled = Enabled, MembershipVerified = MembershipVerified };
            }
            public EngineeringDirectoryIdentity Authenticate(ActiveDirectoryIdentityConfiguration c, string u, string p) { Authentications++; return Read(); }
            public EngineeringDirectoryIdentity Revalidate(ActiveDirectoryIdentityConfiguration c, string sid) { Revalidations++; return Read(); }
        }
        private sealed class IdentityFixture : IDisposable
        {
            internal readonly string Path;
            internal readonly WindowsEngineeringIdentityStore Store;
            internal readonly EngineeringIdentityPolicy Policy;
            internal readonly IdentityDirectoryDouble Directory = new IdentityDirectoryDouble();
            internal readonly ActiveDirectoryIdentityConfiguration Ad;
            internal EngineeringIdentityAuthority Authority;
            internal DateTimeOffset Now = DateTimeOffset.UtcNow;
            internal readonly string Password = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            internal EngineeringIdentitySession Administrator;
            internal const string Administration = "TEST_ONLY_NON_ENGINEERING_IDENTITY_ADMINISTRATION";
            internal const string Project = "TEST_ONLY_NON_ENGINEERING_PROJECT";
            internal IdentityFixture(bool seed = true, bool emergency = false, bool selfApproval = false)
            {
                // Temp/AppData grants Windows application capability SIDs mutation rights on this machine.
                // Use a new direct profile child with a protected DACL; never alter existing OS ACLs.
                Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SewerPilotIdentityTest_" + Guid.NewGuid().ToString("N"));
                string sid = WindowsIdentity.GetCurrent().User.Value;
                WindowsEngineeringIdentityStore.ProvisionDirectory(Path, sid);
                Store = new WindowsEngineeringIdentityStore(Path, sid);
                Policy = new EngineeringIdentityPolicy { IdentityAdministrationProjectId = Administration, LocalEmergencyAccessEnabled = emergency,
                    AllowExceptionalSelfApproval = selfApproval, SelfApprovalPolicyReference = selfApproval ? "TEST_ONLY_EXPLICIT_POLICY" : null,
                    Passwords = new LocalPasswordPolicy { MaximumFailures = 2, LockoutMinutes = 1 } };
                Ad = new ActiveDirectoryIdentityConfiguration { DomainController = "dc.directory.invalid", Domain = "directory.invalid", SearchBase = "DC=directory,DC=invalid",
                    GroupRoleMappings = new System.Collections.Generic.List<EngineeringAdRoleMapping> {
                        new EngineeringAdRoleMapping { GroupSid = Directory.Group, ProjectId = Project, Role = EngineeringProjectRole.Approver } } };
                Authority = Reopen();
                if (seed)
                {
                    Authority.EnrollTestAdministrator("sameuser", Password);
                    Administrator = Authority.Authenticate(EngineeringIdentityProvider.Local, "sameuser", Password);
                }
            }
            internal EngineeringIdentityAuthority Reopen() => new EngineeringIdentityAuthority(Store, Policy, Ad, Directory, () => Now);
            internal EngineeringIdentitySession User(string name, EngineeringProjectRole role, bool emergency = false)
            {
                string id = Authority.CreateLocalAccount(Administrator, name, Password, emergency);
                Authority.SetLocalRole(Administrator, id, Project, role, true);
                return Authority.Authenticate(EngineeringIdentityProvider.Local, name, Password);
            }
            internal GravityExecutionPlan Plan(EngineeringIdentitySession approver)
            {
                var input = ProductionFixture().Input;
                input.Approval.ActorId = approver.IdentityKey;
                input.Approval.ApprovedUtc = Now.ToString("o");
                // ProductionFixture is explicitly TEST_ONLY; no live values, users or approvals are enrolled.
                Check(input.ProjectId == Project, "Existing fixture retains explicit TEST_ONLY project identity.");
                return GravityExecutionPlanBuilder.Build(input);
            }
            public void Dispose()
            {
                string parent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string full = System.IO.Path.GetFullPath(Path);
                if (System.IO.Path.GetDirectoryName(full) != parent || !System.IO.Path.GetFileName(full).StartsWith("SewerPilotIdentityTest_", StringComparison.Ordinal))
                    throw new InvalidOperationException("TEST_CLEANUP_PATH_INVALID");
                if (System.IO.Directory.Exists(full)) System.IO.Directory.Delete(full, true);
            }
        }
        private static void IdentityDenied(Action action, string reason)
        {
            try { action(); Check(false, "Expected identity rejection: " + reason); }
            catch (EngineeringIdentityException error) { Check(error.Message == reason, "Exact fail-closed identity reason: " + reason + "; got " + error.Message); }
        }
        private static void P15Identity_LocalCredentialsAndProtectedStorage()
        {
            using (var f = new IdentityFixture())
            {
                Check(f.Administrator.IdentityKey.StartsWith("LOCAL:", StringComparison.Ordinal), "Local identity is immutable/namespaced.");
                var user = f.User("engineer", EngineeringProjectRole.Engineer);
                Check(f.Authority.HasPermission(user, IdentityFixture.Project, EngineeringPermission.EditDesignBasis), "Valid local credentials grant only explicit role.");
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "engineer", Guid.NewGuid().ToString()), "LOCAL_AUTHENTICATION_REJECTED");
                IdentityDenied(() => f.Authority.CreateLocalAccount(f.Administrator, "ENGINEER", f.Password), "USERNAME_ALREADY_ENROLLED");
                IdentityDenied(() => f.Authority.CreateLocalAccount(f.Administrator, "short", "short"), "PASSWORD_POLICY_REJECTED");
                var state = f.Store.Update(s => s);
                Check(state.Accounts.Select(a => a.Salt).Distinct().Count() == state.Accounts.Count && state.Accounts.All(a => a.Iterations >= 600000), "Unique CSPRNG salts / strong PBKDF2-SHA256 work factor.");
                Check(state.Accounts.All(a => a.PasswordHash != f.Password) && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f.Store.DataPath)).Contains(f.Password), "No plaintext password in protected store.");
                var acl = System.IO.Directory.GetAccessControl(f.Path);
                Check(acl.AreAccessRulesProtected && acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r => r.IdentityReference.Value == WindowsIdentity.GetCurrent().User.Value), "Actual protected Windows DACL allows only authority account.");
                Check(f.Reopen().HasPermission(user, IdentityFixture.Project, EngineeringPermission.Read), "Protected account/session state survives independent authority library instance.");
                Check(f.Store.ReadAudit().All(a => !a.Payload.Values.Any(v => v == f.Password || state.Accounts.Any(u => u.PasswordHash == v || u.Salt == v))), "Audit contains no credentials, salt or password hash.");
            }
        }
        private static void P15Identity_LockoutDisabledAndBootstrap()
        {
            using (var empty = new IdentityFixture(seed: false))
            {
                IdentityDenied(() => empty.Authority.BootstrapAdministrator("admin", empty.Password, new SecurityIdentifier(WellKnownSidType.NullSid, null).Value), "BOOTSTRAP_ADMINISTRATOR_DENIED");
                Check(empty.Store.Update(s => s.Accounts.Count) == 0, "Unauthorized bootstrap leaves zero accounts.");
            }
            using (var f = new IdentityFixture())
            {
                var user = f.User("locked", EngineeringProjectRole.Viewer);
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "locked", "invalid"), "LOCAL_AUTHENTICATION_REJECTED");
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "locked", "invalid"), "LOCAL_AUTHENTICATION_REJECTED");
                IdentityDenied(() => f.Reopen().Authenticate(EngineeringIdentityProvider.Local, "locked", f.Password), "AUTHENTICATION_RATE_LIMITED");
                Check(!f.Authority.HasPermission(user, IdentityFixture.Project, EngineeringPermission.Read), "Locked account cannot reuse prior session rights.");
                f.Now = f.Now.AddMinutes(2);
                Check(f.Reopen().Authenticate(EngineeringIdentityProvider.Local, "locked", f.Password).IdentityKey == user.IdentityKey, "Persistent lockout expires under explicit bounded policy.");
                f.Authority.SetLocalAccountDisabled(f.Administrator, user.IdentityKey, true);
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "locked", f.Password), "LOCAL_AUTHENTICATION_REJECTED");
                IdentityDenied(() => f.Authority.HasPermission(user, IdentityFixture.Project, EngineeringPermission.Read), "AUTHENTICATED_SESSION_REQUIRED");
                IdentityDenied(() => f.Authority.EnrollTestAdministrator("newadmin", f.Password), "BOOTSTRAP_ALREADY_ENROLLED");
            }
        }
        private static void P15Identity_AdSelectionAndFailure()
        {
            using (var f = new IdentityFixture())
            {
                var ad = f.Authority.Authenticate(EngineeringIdentityProvider.ActiveDirectory, "sameuser", f.Password);
                Check(ad.IdentityKey == "AD:" + f.Directory.Sid && ad.IdentityKey != f.Administrator.IdentityKey, "Same username never merges AD and LOCAL identities.");
                Check(f.Authority.HasPermission(ad, IdentityFixture.Project, EngineeringPermission.AuthorizeExecution) && f.Directory.Revalidations > 0, "Current SID/security-group mapping is rechecked for permissions.");
                var engineer = f.User("engineer", EngineeringProjectRole.Engineer); var plan = f.Plan(ad);
                f.Authority.IssueApproval(ad, engineer, plan, f.Now.AddMinutes(10));
                Check(f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "AD actor uses the same exact authoritative approval contract as local actor.");
                foreach (var failure in new[] { "AD_UNAVAILABLE", "AD_CERTIFICATE_INVALID" })
                {
                    f.Directory.Failure = failure;
                    IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.ActiveDirectory, "sameuser", f.Password), failure);
                    Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Directory failure revokes effective approval verification without local fallback.");
                    f.Now = f.Now.AddMinutes(2);
                }
                f.Directory.Failure = null; f.Directory.MembershipVerified = false;
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.ActiveDirectory, "sameuser", f.Password), "AD_MEMBERSHIP_OR_IDENTITY_UNVERIFIED");
                Check(f.Store.Update(s => s.Sessions.Count(t => t.Provider == EngineeringIdentityProvider.Local)) == 2, "Directory failures never auto-authenticate a matching local username.");
                f.Directory.MembershipVerified = true; f.Directory.Enabled = false;
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.ActiveDirectory, "sameuser", f.Password), "AD_MEMBERSHIP_OR_IDENTITY_UNVERIFIED");
                IdentityDenied(() => new ActiveDirectoryIdentityConfiguration { DomainController = "dc.directory.invalid", Domain = "directory.invalid", SearchBase = "DC=invalid", CaTrust = "TrustAll" }.Validate(), "AD_NOT_CONFIGURED");
                IdentityDenied(() => new ActiveDirectoryIdentityConfiguration().Validate(), "AD_NOT_CONFIGURED");
                IdentityDenied(() => f.Authority.Authenticate((EngineeringIdentityProvider)0, "sameuser", f.Password), "PROVIDER_SELECTION_REQUIRED");
            }
        }
        private static void P15Identity_ProjectRbacAndEmergency()
        {
            using (var f = new IdentityFixture(emergency: true))
            {
                var engineer = f.User("engineer", EngineeringProjectRole.Engineer);
                Check(!f.Authority.HasPermission(engineer, "TEST_ONLY_OTHER_PROJECT", EngineeringPermission.Read), "Project roles cannot cross project boundaries.");
                Check(!f.Authority.HasPermission(engineer, IdentityFixture.Project, EngineeringPermission.ApproveCandidate), "Authenticated engineer has no implicit approval.");
                Check(!EngineeringRbac.Allows(EngineeringProjectRole.Administrator, EngineeringPermission.AuthorizeExecution), "Administrator identity management does not implicitly authorize engineering execution.");
                Check(EngineeringRbac.Allows(EngineeringProjectRole.Reviewer, EngineeringPermission.ReviewCriteria) && !EngineeringRbac.Allows(EngineeringProjectRole.Reviewer, EngineeringPermission.EditDesignBasis), "Criteria review and editing are separate permissions.");
                Check(!EngineeringRbac.Allows((EngineeringProjectRole)999, EngineeringPermission.Read), "Unknown role denies by default.");
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "engineer", f.Password, true), "LOCAL_EMERGENCY_ACCESS_DENIED");
                var emergency = f.User("emergency", EngineeringProjectRole.Viewer, emergency: true);
                var session = f.Authority.Authenticate(EngineeringIdentityProvider.Local, "emergency", f.Password, true);
                Check(f.Authority.HasPermission(session, IdentityFixture.Project, EngineeringPermission.Read) && f.Store.ReadAudit().Any(a => a.ActionType == "EmergencyAuthentication"), "Emergency access needs both policy and independent account grant with audit.");
                Check(!f.Authority.HasPermission(session, IdentityFixture.Project, EngineeringPermission.AuthorizeExecution), "Emergency access adds no execution rights.");
                f.Policy.LocalEmergencyAccessEnabled = false;
                Check(!f.Reopen().HasPermission(session, IdentityFixture.Project, EngineeringPermission.Read), "Emergency authorization is rechecked, not cached into session roles.");
            }
        }
        private static void P15Identity_ApprovalBindingsAndSeparation()
        {
            using (var f = new IdentityFixture())
            {
                var submitter = f.User("engineer", EngineeringProjectRole.Engineer); var approver = f.User("approver", EngineeringProjectRole.Approver);
                var plan = f.Plan(approver);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Forged approval DTO has no authoritative record.");
                IdentityDenied(() => f.Authority.IssueApproval(submitter, submitter, plan, f.Now.AddMinutes(2)), "ENGINEERING_APPROVAL_PERMISSION_DENIED");
                f.Authority.SetLocalRole(f.Administrator, approver.IdentityKey, IdentityFixture.Project, EngineeringProjectRole.Engineer, true);
                IdentityDenied(() => f.Authority.IssueApproval(approver, approver, plan, f.Now.AddMinutes(2)), "SELF_APPROVAL_DENIED");
                var stale = ProductionFixture().Input; stale.Approval.ActorId = approver.IdentityKey;
                IdentityDenied(() => f.Authority.IssueApproval(approver, submitter, GravityExecutionPlanBuilder.Build(stale), f.Now.AddMinutes(2)), "ENGINEERING_APPROVAL_STALE");
                f.Authority.IssueApproval(approver, submitter, plan, f.Now.AddMinutes(2));
                Check(f.Reopen().Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Authoritative exact approval survives reopen.");
                var changed = plan.Inputs; changed.Approval.Calculate = false;
                var altered = GravityExecutionPlanBuilder.Build(changed);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(altered)).Approved, "Calculation intent/modified plan rejected.");
                var wrongActor = plan.Inputs; wrongActor.Approval.ActorId = submitter.IdentityKey;
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(GravityExecutionPlanBuilder.Build(wrongActor))).Approved, "Identity mismatch rejected against authoritative record.");
                var wrongProject = plan.Inputs;
                wrongProject.ProjectId = wrongProject.DesignBasis.CommonDesignBasis.ProjectId = wrongProject.Approval.ProjectId = "TEST_ONLY_OTHER_PROJECT";
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(GravityExecutionPlanBuilder.Build(wrongProject))).Approved, "Project mismatch rejected.");
                var other = ProductionFixture(free: true).Input; other.Approval.ActorId = approver.IdentityKey; other.Approval.ApprovalId = plan.Inputs.Approval.ApprovalId;
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(GravityExecutionPlanBuilder.Build(other))).Approved, "Candidate mismatch rejected.");
                Check(f.Store.ReadAudit().Any(a => a.ActionType == "ApprovalIssued") && f.Store.ReadAudit().Any(a => a.ActionType == "ApprovalVerification.Rejected"), "Approval issue/verify/reject audited.");
            }
        }
        private static void P15Identity_ApprovalRevocationAndExpiry()
        {
            using (var f = new IdentityFixture())
            {
                var engineer = f.User("engineer", EngineeringProjectRole.Engineer); var approver = f.User("approver", EngineeringProjectRole.Approver); var plan = f.Plan(approver);
                f.Authority.IssueApproval(approver, engineer, plan, f.Now.AddMinutes(2));
                f.Authority.RevokeApproval(approver, plan.Inputs.Approval.ApprovalId);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Revoked approval rejected immediately.");
                var next = plan.Inputs; next.Approval.ApprovalId = Guid.NewGuid().ToString("D"); var second = GravityExecutionPlanBuilder.Build(next);
                f.Authority.IssueApproval(approver, engineer, second, f.Now.AddMinutes(1)); f.Now = f.Now.AddMinutes(2);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(second)).Approved, "Expired authoritative approval rejected.");
                Check(f.Store.ReadAudit().Any(a => a.ActionType == "ApprovalRevoked"), "Approval revocation audited.");
            }
            using (var f = new IdentityFixture(selfApproval: true))
            {
                var approver = f.User("exception", EngineeringProjectRole.Approver);
                f.Authority.SetLocalRole(f.Administrator, approver.IdentityKey, IdentityFixture.Project, EngineeringProjectRole.Engineer, true);
                f.Authority.IssueApproval(approver, approver, f.Plan(approver), f.Now.AddMinutes(1));
                Check(f.Store.ReadAudit().Any(a => a.ActionType == "ExceptionalSelfApprovalIssued" && a.Payload["SelfApprovalPolicyReference"] == "TEST_ONLY_EXPLICIT_POLICY"), "Exceptional self-approval requires explicit policy and traceable audit.");
            }
        }
        private static void P15Identity_RuntimeBoundaryAndTampering()
        {
            using (var f = new IdentityFixture())
            {
                var engineer = f.User("engineer", EngineeringProjectRole.Engineer); var approver = f.User("approver", EngineeringProjectRole.Approver); var plan = f.Plan(approver);
                f.Authority.IssueApproval(approver, engineer, plan, f.Now.AddMinutes(1));
                Check(f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).TestOnly && GravityEngineeringApprovalComposition.RuntimeVerifier == null, "Same-privilege library is never promoted to independent runtime authority.");
                BlockedBecause(() => new GravityEngineeringExecutionService(f.Authority).ExecuteEngineeringPlan(plan, null, null, 0, null), "TEST_ONLY_NON_ENGINEERING");
                BlockedBecause(() => GravityEngineeringAuthorization.RequireRuntimeApproval(plan), "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE");
                var bytes = File.ReadAllBytes(f.Store.DataPath); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(f.Store.DataPath, bytes);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "DPAPI protected-record tampering denies approval.");
                IdentityDenied(() => f.Authority.Authenticate(EngineeringIdentityProvider.Local, "approver", f.Password), "PROTECTED_IDENTITY_STORE_UNAVAILABLE_OR_CORRUPT");
            }
            using (var f = new IdentityFixture())
            {
                var acl = System.IO.Directory.GetAccessControl(f.Path);
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
                System.IO.Directory.SetAccessControl(f.Path, acl);
                IdentityDenied(() => f.Store.ReadAudit(), "STORE_ACL_INVALID");
            }
        }
        private static void P15Identity_SessionsAndAuthoritativeRecheck()
        {
            using (var f = new IdentityFixture())
            {
                var engineer = f.User("engineer", EngineeringProjectRole.Engineer); var approver = f.User("approver", EngineeringProjectRole.Approver); var plan = f.Plan(approver);
                f.Authority.IssueApproval(approver, engineer, plan, f.Now.AddMinutes(2));
                f.Authority.SetLocalRole(f.Administrator, approver.IdentityKey, IdentityFixture.Project, EngineeringProjectRole.Approver, false);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Permissions revoked after issuance are rechecked before execution.");
                f.Authority.SetLocalRole(f.Administrator, approver.IdentityKey, IdentityFixture.Project, EngineeringProjectRole.Approver, true);
                f.Authority.SetLocalAccountDisabled(f.Administrator, approver.IdentityKey, true);
                Check(!f.Authority.Verify(new GravityEngineeringApprovalBinding(plan)).Approved, "Disabled actor cannot retain issued approval rights.");
                IdentityDenied(() => f.Authority.IssueApproval(approver, engineer, plan, f.Now.AddMinutes(1)), "AUTHENTICATED_SESSION_REQUIRED");
                var ad = f.Authority.Authenticate(EngineeringIdentityProvider.ActiveDirectory, "sameuser", f.Password);
                f.Directory.Failure = "AD_UNAVAILABLE";
                IdentityDenied(() => f.Authority.HasPermission(ad, IdentityFixture.Project, EngineeringPermission.AuthorizeExecution), "AD_UNAVAILABLE");
                f.Authority.RevokeSession(engineer);
                IdentityDenied(() => f.Authority.HasPermission(engineer, IdentityFixture.Project, EngineeringPermission.Read), "AUTHENTICATED_SESSION_REQUIRED");
                f.Now = f.Now.AddMinutes(16);
                IdentityDenied(() => f.Authority.HasPermission(f.Administrator, IdentityFixture.Administration, EngineeringPermission.ManageIdentities), "AUTHENTICATED_SESSION_REQUIRED");
            }
        }
    }
}
