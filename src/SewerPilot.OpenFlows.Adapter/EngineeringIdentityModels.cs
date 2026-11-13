using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum EngineeringIdentityProvider { ActiveDirectory = 1, Local = 2 }
    public enum EngineeringProjectRole { Viewer = 1, Engineer, Reviewer, Approver, Administrator }
    public enum EngineeringPermission { Read = 1, EditDesignBasis, ReviewCriteria, ApproveCandidate, AuthorizeExecution, ManageIdentities }

    public sealed class LocalPasswordPolicy
    {
        public int MinimumLength { get; set; } = 15;
        public int MaximumLength { get; set; } = 128;
        public int Pbkdf2Iterations { get; set; } = 600000;
        public int MaximumFailures { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;
        internal void Validate()
        {
            IdentityGuard.Require(MinimumLength >= 15 && MaximumLength >= MinimumLength && MaximumLength <= 1024 &&
                Pbkdf2Iterations >= 600000 && Pbkdf2Iterations <= 2000000 && MaximumFailures >= 1 && MaximumFailures <= 10 &&
                LockoutMinutes >= 1 && LockoutMinutes <= 1440, "PASSWORD_POLICY_INVALID");
        }
    }
    public sealed class ActiveDirectoryIdentityConfiguration
    {
        public string DomainController { get; set; }
        public int Port { get; set; } = 636;
        public string Domain { get; set; }
        public string SearchBase { get; set; }
        // Administrators configure the OS CA trust store. No custom permissive certificate callback.
        public string CaTrust { get; set; } = "WindowsMachineTrustStore";
        public int TimeoutSeconds { get; set; } = 10;
        public List<EngineeringAdRoleMapping> GroupRoleMappings { get; set; } = new List<EngineeringAdRoleMapping>();
        internal void Validate()
        {
            IdentityGuard.Require(!string.IsNullOrWhiteSpace(DomainController) && Uri.CheckHostName(DomainController) == UriHostNameType.Dns &&
                !string.IsNullOrWhiteSpace(Domain) && Uri.CheckHostName(Domain) == UriHostNameType.Dns && !string.IsNullOrWhiteSpace(SearchBase) && Port > 0 && Port <= 65535 &&
                CaTrust == "WindowsMachineTrustStore" && TimeoutSeconds >= 1 && TimeoutSeconds <= 30 && GroupRoleMappings != null, "AD_NOT_CONFIGURED");
            string domainBase = string.Join(",", Domain.Split('.').Select(p => "DC=" + p));
            IdentityGuard.Require(SearchBase.Equals(domainBase, StringComparison.OrdinalIgnoreCase) ||
                SearchBase.EndsWith("," + domainBase, StringComparison.OrdinalIgnoreCase), "AD_SEARCH_DOMAIN_MISMATCH");
            foreach (var m in GroupRoleMappings)
                IdentityGuard.Require(m != null && IdentityGuard.ValidSid(m.GroupSid) && !string.IsNullOrWhiteSpace(m.ProjectId) &&
                    Enum.IsDefined(typeof(EngineeringProjectRole), m.Role), "AD_MAPPING_INVALID");
        }
    }
    public sealed class EngineeringAdRoleMapping
    {
        public string GroupSid { get; set; }
        public string ProjectId { get; set; }
        public EngineeringProjectRole Role { get; set; }
    }
    public sealed class EngineeringIdentityPolicy
    {
        public string IdentityAdministrationProjectId { get; set; }
        public LocalPasswordPolicy Passwords { get; set; } = new LocalPasswordPolicy();
        public int SessionMinutes { get; set; } = 15;
        public bool LocalEmergencyAccessEnabled { get; set; }
        public bool AllowExceptionalSelfApproval { get; set; }
        public string SelfApprovalPolicyReference { get; set; }
        internal void Validate()
        {
            IdentityGuard.Require(!string.IsNullOrWhiteSpace(IdentityAdministrationProjectId) && Passwords != null && SessionMinutes >= 1 && SessionMinutes <= 60 &&
                (!AllowExceptionalSelfApproval || !string.IsNullOrWhiteSpace(SelfApprovalPolicyReference)), "IDENTITY_POLICY_INVALID");
            Passwords.Validate();
        }
    }
    public sealed class EngineeringIdentitySession
    {
        internal EngineeringIdentitySession(string id, string token, string identity, EngineeringIdentityProvider provider, DateTimeOffset until)
        { Id = id; Token = token; IdentityKey = identity; Provider = provider; ExpiresUtc = until; }
        public string Id { get; }
        internal string Token { get; }
        public string IdentityKey { get; }
        public EngineeringIdentityProvider Provider { get; }
        public DateTimeOffset ExpiresUtc { get; }
    }
    public sealed class EngineeringIdentityException : Exception
    {
        public EngineeringIdentityException(string code) : base(code) { }
    }
    public static class EngineeringRbac
    {
        public static bool Allows(EngineeringProjectRole role, EngineeringPermission permission)
        {
            switch (role)
            {
                case EngineeringProjectRole.Viewer: return permission == EngineeringPermission.Read;
                case EngineeringProjectRole.Engineer: return permission == EngineeringPermission.Read || permission == EngineeringPermission.EditDesignBasis;
                case EngineeringProjectRole.Reviewer: return permission == EngineeringPermission.Read || permission == EngineeringPermission.ReviewCriteria;
                case EngineeringProjectRole.Approver: return permission == EngineeringPermission.Read || permission == EngineeringPermission.ApproveCandidate || permission == EngineeringPermission.AuthorizeExecution;
                case EngineeringProjectRole.Administrator: return permission == EngineeringPermission.Read || permission == EngineeringPermission.ManageIdentities;
                default: return false;
            }
        }
    }
    internal static class IdentityGuard
    {
        internal static void Require(bool condition, string code) { if (!condition) throw new EngineeringIdentityException(code); }
        internal static bool ValidSid(string value)
        { try { return new System.Security.Principal.SecurityIdentifier(value).Value == value; } catch { return false; } }
        internal static string Username(string value)
        {
            Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'), "USERNAME_INVALID");
            return value.ToUpperInvariant();
        }
        internal static byte[] Random(int size) { var bytes = new byte[size]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return bytes; }
        internal static string Hash(string value) { using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        internal static bool Equal(byte[] a, byte[] b)
        { if (a == null || b == null || a.Length != b.Length) return false; int result = 0; for (int i = 0; i < a.Length; i++) result |= a[i] ^ b[i]; return result == 0; }
        internal static string PasswordHash(string password, string salt, int iterations)
        { using (var kdf = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256)) return Convert.ToBase64String(kdf.GetBytes(32)); }
    }
    internal sealed class EngineeringIdentityState
    {
        public int Schema { get; set; } = 1;
        public long Revision { get; set; }
        public List<EngineeringLocalAccount> Accounts { get; set; } = new List<EngineeringLocalAccount>();
        public List<EngineeringRoleGrant> Grants { get; set; } = new List<EngineeringRoleGrant>();
        public List<EngineeringStoredSession> Sessions { get; set; } = new List<EngineeringStoredSession>();
        public List<EngineeringStoredApproval> Approvals { get; set; } = new List<EngineeringStoredApproval>();
        public List<AuditRecord> Audit { get; set; } = new List<AuditRecord>();
        public int AuthenticationFailures { get; set; }
        public DateTime AuthenticationLockedUntil { get; set; }
    }
    internal sealed class EngineeringLocalAccount
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Salt { get; set; }
        public string PasswordHash { get; set; }
        public int Iterations { get; set; }
        public bool Disabled { get; set; }
        public int Failures { get; set; }
        public DateTime LockedUntil { get; set; }
        public bool EmergencyAuthorized { get; set; }
    }
    internal sealed class EngineeringRoleGrant
    {
        public string IdentityKey { get; set; }
        public string ProjectId { get; set; }
        public EngineeringProjectRole Role { get; set; }
    }
    internal sealed class EngineeringStoredSession
    {
        public string Id { get; set; }
        public string TokenHash { get; set; }
        public string IdentityKey { get; set; }
        public EngineeringIdentityProvider Provider { get; set; }
        public DateTime AuthenticatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public bool Revoked { get; set; }
        public bool Emergency { get; set; }
    }
    internal sealed class EngineeringStoredApproval
    {
        public string ApprovalId { get; set; }
        public string BindingJson { get; set; }
        public string ApproverSessionId { get; set; }
        public string SubmitterIdentity { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ValidUntilUtc { get; set; }
        public bool Revoked { get; set; }
    }
}
